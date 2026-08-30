using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

/// <summary>
/// Two artifacts rendering the same Kinds through one shared set of macros — the arrangement
/// the tool's promise depends on, and the one that was impossible while every template was an
/// island.
/// </summary>
public class SharedMacroTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public SharedMacroTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_partials_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        _outputDir = Path.Combine(_tempDir, "output");

        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product", "Kind": "Class", "Children": [
                    { "Name": "Id",    "Kind": "Property", "Type": "int" },
                    { "Name": "Price", "Kind": "Property", "Type": "decimal" }
                ] }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private const string SharedMacros =
        "{%- macro DefaultProperty(p) %}shared:{{ p.Name }}:{{ p.Type }}{%- endmacro %}";

    private const string RendersChildren =
        "{%- for c in item.Children %}{% dispatch c %}|{%- endfor %}";

    private void Write(string relative, string content) =>
        File.WriteAllText(Path.Combine(_tempDir, relative), content);

    private ServerContext Configure(
        List<string>? partials = null,
        Dictionary<string, TemplateConfig>? templates = null)
    {
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Partials = partials ?? ["templates/_macros.liquid"],
            Templates = templates ?? new Dictionary<string, TemplateConfig>
            {
                ["entity"] = Template("templates/entity.liquid", "{{ item.Name }}.cs")
            }
        });

        return new ServerContext(_tempDir);
    }

    private static TemplateConfig Template(string path, string pattern) => new()
    {
        Path = path,
        OutputPattern = pattern,
        Scope = "PerItem",
        Mode = "Always",
        AppliesTo = "Class"
    };

    private string Output(string name) => File.ReadAllText(Path.Combine(_outputDir, name));
    private static JsonElement Json(string result) => JsonDocument.Parse(result).RootElement;

    // --- composition ---------------------------------------------------------

    [Fact]
    public void AMacroInAPartial_IsFoundByDispatch()
    {
        // Liquid's own include renders in a child scope and the macro is discarded before
        // dispatch looks for it. Composition is what makes this work at all.
        Write("templates/_macros.liquid", SharedMacros);
        Write("templates/entity.liquid", RendersChildren);

        GenerateTool.Execute(Configure());

        Output("Product.cs").ShouldBe("shared:Id:int|shared:Price:decimal|");
    }

    [Fact]
    public void OneMacroServesEveryArtifactThatSharesIt()
    {
        Write("templates/_macros.liquid", SharedMacros);
        Write("templates/entity.liquid", RendersChildren);
        Write("templates/dto.liquid", RendersChildren);

        GenerateTool.Execute(Configure(templates: new Dictionary<string, TemplateConfig>
        {
            ["entity"] = Template("templates/entity.liquid", "{{ item.Name }}.cs"),
            ["dto"] = Template("templates/dto.liquid", "{{ item.Name }}Dto.cs")
        }));

        Output("Product.cs").ShouldBe(Output("ProductDto.cs"));
        Output("ProductDto.cs").ShouldContain("shared:Id:int");
    }

    [Fact]
    public void ATemplatesOwnMacroShadowsTheSharedOne()
    {
        // Partials come first and the template last, so the artifact can override the default.
        Write("templates/_macros.liquid", SharedMacros);
        Write("templates/entity.liquid",
            "{%- macro DefaultProperty(p) %}local:{{ p.Name }}{%- endmacro %}" + RendersChildren);

        GenerateTool.Execute(Configure());

        Output("Product.cs").ShouldBe("local:Id|local:Price|");
    }

    [Fact]
    public void PartialsApplyInDeclaredOrder()
    {
        Write("templates/_first.liquid", "{%- macro DefaultProperty(p) %}first{%- endmacro %}");
        Write("templates/_second.liquid", "{%- macro DefaultProperty(p) %}second{%- endmacro %}");
        Write("templates/entity.liquid", RendersChildren);

        GenerateTool.Execute(Configure(["templates/_first.liquid", "templates/_second.liquid"]));

        Output("Product.cs").ShouldBe("second|second|");
    }

    [Fact]
    public void NoPartials_LeavesTemplatesExactlyAsTheyWere()
    {
        Write("templates/entity.liquid",
            "{%- macro DefaultProperty(p) %}{{ p.Name }}{%- endmacro %}" + RendersChildren);

        GenerateTool.Execute(Configure([]));

        Output("Product.cs").ShouldBe("Id|Price|");
    }

    // --- caching -------------------------------------------------------------

    [Fact]
    public void EditingAPartial_RecompilesEveryTemplateThatSharesIt()
    {
        // The trap: caching on the template's own timestamp alone serves a stale compilation,
        // so the first run after a shared macro changes silently renders the old one.
        Write("templates/_macros.liquid", SharedMacros);
        Write("templates/entity.liquid", RendersChildren);

        var ctx = Configure();
        GenerateTool.Execute(ctx);
        Output("Product.cs").ShouldContain("shared:");

        Write("templates/_macros.liquid", "{%- macro DefaultProperty(p) %}edited:{{ p.Name }}{%- endmacro %}");
        File.SetLastWriteTimeUtc(Path.Combine(_tempDir, "templates", "_macros.liquid"), DateTime.UtcNow.AddSeconds(1));

        GenerateTool.Execute(ctx);

        Output("Product.cs").ShouldBe("edited:Id|edited:Price|");
    }

    // --- validation ----------------------------------------------------------

    [Fact]
    public void AVariantMacroInAPartial_IsNotReportedAsMissing()
    {
        // The check this protects is the one that catches a silently-ignored override. If it
        // cannot see partials it fires on correct projects, which is worse than not firing.
        Write("templates/_macros.liquid",
            SharedMacros + "\n{%- macro CurrencyProperty(p) %}money{%- endmacro %}");
        Write("templates/entity.liquid", RendersChildren);

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Partials = ["templates/_macros.liquid"],
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = Template("templates/entity.liquid", "{{ item.Name }}.cs")
            },
            Overrides = [new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Currency" }]
        });

        var result = Json(ValidateConfigTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("Valid").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void AMisspelledVariantIsStillCaughtWhenPartialsAreInPlay()
    {
        Write("templates/_macros.liquid",
            SharedMacros + "\n{%- macro CurrencyProperty(p) %}money{%- endmacro %}");
        Write("templates/entity.liquid", RendersChildren);

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Partials = ["templates/_macros.liquid"],
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = Template("templates/entity.liquid", "{{ item.Name }}.cs")
            },
            Overrides = [new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Curency" }]
        });

        var result = Json(ValidateConfigTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        var error = result.GetProperty("Errors").EnumerateArray().Select(e => e.GetString()!).ShouldHaveSingleItem();
        error.ShouldContain("declares no macro 'CurencyProperty'");
        error.ShouldContain("Did you mean 'CurrencyProperty'?");
    }

    [Fact]
    public void ABrokenPartial_IsReportedAgainstThePartialNotEveryTemplate()
    {
        Write("templates/_macros.liquid", "{% for x in %}");
        Write("templates/entity.liquid", RendersChildren);
        Write("templates/dto.liquid", RendersChildren);

        var result = Json(ValidateConfigTool.Execute(Configure(templates: new Dictionary<string, TemplateConfig>
        {
            ["entity"] = Template("templates/entity.liquid", "{{ item.Name }}.cs"),
            ["dto"] = Template("templates/dto.liquid", "{{ item.Name }}Dto.cs")
        })));

        var errors = result.GetProperty("Errors").EnumerateArray().Select(e => e.GetString()!).ToList();

        errors.ShouldContain(e => e.StartsWith("Partial 'templates/_macros.liquid'"));
        errors.ShouldNotContain(e => e.StartsWith("Template 'entity'"));
        errors.ShouldNotContain(e => e.StartsWith("Template 'dto'"));
    }

    [Fact]
    public void AMissingPartial_IsAnError()
    {
        Write("templates/entity.liquid", RendersChildren);

        var result = Json(ValidateConfigTool.Execute(Configure(["templates/_nope.liquid"])));

        result.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.GetProperty("Errors").EnumerateArray().Select(e => e.GetString()!)
            .ShouldContain(e => e.Contains("templates/_nope.liquid") && e.Contains("File not found"));
    }

    [Fact]
    public void APartialPathEscapingTheProject_IsRefused()
    {
        Write("templates/entity.liquid", RendersChildren);

        var result = Json(ValidateConfigTool.Execute(Configure(["../../etc/passwd"])));

        result.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.GetProperty("Errors").EnumerateArray().Select(e => e.GetString()!)
            .ShouldContain(e => e.Contains("Refusing"));
    }

    [Fact]
    public void AnUnknownFilterInAPartial_IsWarnedAbout()
    {
        Write("templates/_macros.liquid", "{%- macro DefaultProperty(p) %}{{ p.Name | nonsense }}{%- endmacro %}");
        Write("templates/entity.liquid", RendersChildren);

        Json(ValidateConfigTool.Execute(Configure()))
            .GetProperty("Warnings").EnumerateArray().Select(w => w.GetString()!)
            .ShouldContain(w => w.Contains("Unknown filter 'nonsense'"));
    }

    // --- fit with the dry run ------------------------------------------------

    [Fact]
    public void ChangingASharedMacro_ShowsItsBlastRadiusInADryRun()
    {
        // One edit, many artifacts — the promise, and the risk. The dry run is what makes it
        // reviewable before it lands.
        Write("templates/_macros.liquid", SharedMacros);
        Write("templates/entity.liquid", RendersChildren);
        Write("templates/dto.liquid", RendersChildren);

        var templates = new Dictionary<string, TemplateConfig>
        {
            ["entity"] = Template("templates/entity.liquid", "{{ item.Name }}.cs"),
            ["dto"] = Template("templates/dto.liquid", "{{ item.Name }}Dto.cs")
        };
        var ctx = Configure(templates: templates);
        GenerateTool.Execute(ctx);

        Write("templates/_macros.liquid", "{%- macro DefaultProperty(p) %}reworked:{{ p.Name }}{%- endmacro %}");
        File.SetLastWriteTimeUtc(Path.Combine(_tempDir, "templates", "_macros.liquid"), DateTime.UtcNow.AddSeconds(1));

        var preview = Json(GenerateTool.Execute(Configure(templates: templates), dryRun: true));

        preview.GetProperty("WouldOverwrite").GetInt32().ShouldBe(2);

        var diffs = preview.GetProperty("FilesPlanned").EnumerateArray()
            .Select(f => f.GetProperty("Diff").GetString()!)
            .ToList();

        diffs.Count.ShouldBe(2);
        diffs.ShouldAllBe(d => d.Contains("-shared:Id:int") && d.Contains("+reworked:Id"));
    }
}
