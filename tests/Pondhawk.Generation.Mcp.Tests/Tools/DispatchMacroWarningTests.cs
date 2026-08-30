using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

/// <summary>
/// validate_config warning about a Kind nested under what a template renders that has no macro
/// to render it. Advisory by nature — dispatch is a runtime lookup — so most of these tests
/// are about the cases it must stay quiet for.
/// </summary>
public class DispatchMacroWarningTests : IDisposable
{
    private readonly string _tempDir;

    public DispatchMacroWarningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_dispatchwarn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));

        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product", "Kind": "Class", "Children": [
                    { "Name": "Id",    "Kind": "Property" },
                    { "Name": "Price", "Kind": "Property" },
                    { "Name": "Audit", "Kind": "Attribute" } ] },
                { "Name": "Summary", "Kind": "View", "Children": [
                    { "Name": "Column", "Kind": "Column" } ] }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private const string Dispatches = "{%- for c in item.Children %}{% dispatch c %}{%- endfor %}";

    private List<string> Warnings(
        string template,
        string? appliesTo = "Class",
        List<string>? partials = null,
        string partialSource = "")
    {
        File.WriteAllText(Path.Combine(_tempDir, "templates", "entity.liquid"), template);
        if (partials is not null)
            File.WriteAllText(Path.Combine(_tempDir, "templates", "_macros.liquid"), partialSource);

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Partials = partials ?? [],
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = appliesTo
                }
            }
        });

        return JsonDocument.Parse(ValidateConfigTool.Execute(new ServerContext(_tempDir)))
            .RootElement.GetProperty("Warnings").EnumerateArray()
            .Select(w => w.GetString()!)
            .ToList();
    }

    // --- it fires ------------------------------------------------------------

    [Fact]
    public void WarnsAboutAChildKindWithNoMacro()
    {
        var warnings = Warnings(Dispatches);

        warnings.ShouldContain(w => w.Contains("DefaultProperty") && w.Contains("2 'Property' node(s)"));
        warnings.ShouldContain(w => w.Contains("DefaultAttribute") && w.Contains("1 'Attribute' node(s)"));
    }

    [Fact]
    public void TheWarningSaysWhatWillHappen()
    {
        Warnings(Dispatches)
            .ShouldContain(w => w.Contains("model.json") && w.Contains("fails the file"));
    }

    // --- it stays quiet ------------------------------------------------------

    [Fact]
    public void SaysNothingWhenTheMacroExists()
    {
        var warnings = Warnings(
            "{%- macro DefaultProperty(p) %}x{%- endmacro %}"
            + "{%- macro DefaultAttribute(a) %}y{%- endmacro %}" + Dispatches);

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void SaysNothingWhenTheMacroComesFromAPartial()
    {
        // Macro extraction reads the composed source, so a shared macro counts as declared.
        var warnings = Warnings(
            Dispatches,
            partials: ["templates/_macros.liquid"],
            partialSource: "{%- macro DefaultProperty(p) %}x{%- endmacro %}"
                           + "{%- macro DefaultAttribute(a) %}y{%- endmacro %}");

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void SaysNothingAboutATemplateThatNeverDispatches()
    {
        // It cannot fail this way, so there is nothing to warn about.
        Warnings("class {{ item.Name }} { }").ShouldBeEmpty();
    }

    [Fact]
    public void SaysNothingAboutTheKindTheTemplateItselfRenders()
    {
        // A Class node arrives as `item` and is rendered by the template body. Expecting a
        // DefaultClass macro for it would fire on every correctly written template.
        Warnings(Dispatches).ShouldNotContain(w => w.Contains("DefaultClass"));
    }

    [Fact]
    public void SaysNothingAboutKindsUnderRootsThisTemplateDoesNotRender()
    {
        // Column lives under View, and this template applies to Class.
        Warnings(Dispatches).ShouldNotContain(w => w.Contains("DefaultColumn"));
    }

    [Fact]
    public void ATemplateApplyingToAllSeesEveryRootsChildren()
    {
        var warnings = Warnings(Dispatches, appliesTo: null);

        warnings.ShouldContain(w => w.Contains("DefaultColumn"));
        warnings.ShouldContain(w => w.Contains("DefaultProperty"));
    }

    [Fact]
    public void SaysNothingWhenThereIsNoModelToCheckAgainst()
    {
        File.Delete(Path.Combine(_tempDir, "model.json"));

        Warnings(Dispatches).ShouldNotContain(w => w.Contains("Default"));
    }

    [Fact]
    public void TheWarningIsAdviceNotAnError()
    {
        // Dispatch reaching a node is a runtime question. This must never fail a build.
        File.WriteAllText(Path.Combine(_tempDir, "templates", "entity.liquid"), Dispatches);
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = "Class"
                }
            }
        });

        JsonDocument.Parse(ValidateConfigTool.Execute(new ServerContext(_tempDir)))
            .RootElement.GetProperty("Valid").GetBoolean().ShouldBeTrue();
    }
}
