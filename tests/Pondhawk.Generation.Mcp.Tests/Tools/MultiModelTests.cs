using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

/// <summary>
/// A project with two unrelated generation concerns: entities maintained by hand, and an API
/// surface that would be regenerated from a spec. They share nothing — not a root name, not a
/// Kind vocabulary — which is the reason they are separate documents rather than two halves of
/// one model.json.
/// </summary>
public class MultiModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public MultiModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_multimodel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        _outputDir = Path.Combine(_tempDir, "output");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private const string EntityModel = """
        {
          "Name": "Catalog",
          "Nodes": [
            { "Name": "Product", "Kind": "Class",
              "Children": [ { "Name": "Price", "Kind": "Property", "Type": "decimal" } ] }
          ]
        }
        """;

    private const string ApiModel = """
        {
          "Name": "PublicApi",
          "Nodes": [
            { "Name": "Orders", "Kind": "Resource",
              "Children": [ { "Name": "List", "Kind": "Operation", "Verb": "GET" } ] }
          ]
        }
        """;

    private void WriteModel(string file, string json) =>
        File.WriteAllText(Path.Combine(_tempDir, file), json);

    private void WriteTemplate(string file, string content) =>
        File.WriteAllText(Path.Combine(_tempDir, "templates", file), content);

    private ServerContext Configure(ProjectConfiguration config)
    {
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);
        return new ServerContext(_tempDir);
    }

    /// <summary>The canonical two-concern project used by most tests here.</summary>
    private ServerContext TwoModelProject(List<OverrideConfig>? overrides = null)
    {
        WriteModel("model.json", EntityModel);
        WriteModel("api.model.json", ApiModel);
        WriteTemplate("entity.liquid", "entity {{ model.Name }} {{ item.Name }}");
        WriteTemplate("client.liquid", "client {{ model.Name }} {{ item.Name }}");

        return Configure(new ProjectConfiguration
        {
            OutputDir = _outputDir,
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = "Class"
                },
                ["api-client"] = new()
                {
                    Path = "templates/client.liquid",
                    OutputPattern = "{{ item.Name }}Client.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = "Resource",
                    Model = "api.model.json"
                }
            },
            Overrides = overrides ?? []
        });
    }

    // --- defaults ------------------------------------------------------------

    [Fact]
    public void TemplateWithoutModel_ReadsModelJson()
    {
        new TemplateConfig().ModelFile.ShouldBe("model.json");
        new TemplateConfig { Model = "  " }.ModelFile.ShouldBe("model.json");
        new TemplateConfig { Model = "api.model.json" }.ModelFile.ShouldBe("api.model.json");
    }

    [Fact]
    public void ModelFile_IsNotWrittenBackIntoTheConfig()
    {
        // It is derived, and the schema forbids unknown properties — serializing it would make
        // every config that round-trips through init or update fail its own validation.
        var json = ProjectConfigurationLoader.Serialize(new ProjectConfiguration
        {
            OutputDir = "out",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "x.cs", Scope = "PerItem", Mode = "Always" }
            }
        });

        json.ShouldNotContain("ModelFile");
    }

    // --- generate ------------------------------------------------------------

    [Fact]
    public void Generate_RendersEachTemplateAgainstItsOwnModel()
    {
        var result = JsonDocument.Parse(GenerateTool.Execute(TwoModelProject())).RootElement;

        result.GetProperty("Success").GetBoolean().ShouldBeTrue();
        result.GetProperty("Created").GetInt32().ShouldBe(2);

        File.ReadAllText(Path.Combine(_outputDir, "Product.cs")).ShouldBe("entity Catalog Product");
        File.ReadAllText(Path.Combine(_outputDir, "OrdersClient.cs")).ShouldBe("client PublicApi Orders");
    }

    [Fact]
    public void Generate_GivesEachTemplateItsOwnModelRoot()
    {
        // The root name is part of the document. Two concerns in one file would have to share
        // one, which is the modelling loss that separate models exist to avoid.
        GenerateTool.Execute(TwoModelProject());

        File.ReadAllText(Path.Combine(_outputDir, "Product.cs")).ShouldContain("Catalog");
        File.ReadAllText(Path.Combine(_outputDir, "OrdersClient.cs")).ShouldContain("PublicApi");
    }

    [Fact]
    public void Generate_TemplateFilterStillSelectsASingleConcern()
    {
        GenerateTool.Execute(TwoModelProject(), templates: ["api-client"]);

        File.Exists(Path.Combine(_outputDir, "OrdersClient.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(_outputDir, "Product.cs")).ShouldBeFalse();
    }

    [Fact]
    public void Generate_MissingNamedModel_FailsLoudlyAndNamesTheTemplate()
    {
        WriteModel("model.json", EntityModel);
        WriteTemplate("entity.liquid", "x");
        var ctx = Configure(new ProjectConfiguration
        {
            OutputDir = _outputDir,
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["api-client"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    Model = "api.model.json"
                }
            }
        });

        var message = Should.Throw<InvalidOperationException>(() => GenerateTool.Execute(ctx)).Message;

        message.ShouldContain("api.model.json");
        message.ShouldContain("api-client");
    }

    [Fact]
    public void Generate_ReloadsOnlyTheModelThatChanged()
    {
        var ctx = TwoModelProject();
        GenerateTool.Execute(ctx);

        // Two models are live at once; editing one must not serve the other from a stale entry
        // or evict it. This is the case a single-slot cache got wrong.
        WriteModel("api.model.json", ApiModel.Replace("Orders", "Invoices"));
        File.SetLastWriteTimeUtc(Path.Combine(_tempDir, "api.model.json"), DateTime.UtcNow.AddSeconds(1));

        GenerateTool.Execute(ctx);

        File.Exists(Path.Combine(_outputDir, "InvoicesClient.cs")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(_outputDir, "Product.cs")).ShouldBe("entity Catalog Product");
    }

    // --- validate_config -----------------------------------------------------

    [Fact]
    public void Validate_TwoModelProject_IsClean()
    {
        var result = JsonDocument.Parse(ValidateConfigTool.Execute(TwoModelProject())).RootElement;

        result.GetProperty("Valid").GetBoolean().ShouldBeTrue();
        result.GetProperty("Warnings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void Validate_AppliesToIsCheckedAgainstTheTemplatesOwnModel()
    {
        // 'Resource' exists only in api.model.json. Checking every template against model.json
        // would report a false warning here — and miss a real one on the other side.
        var ctx = TwoModelProject();

        var warnings = Warnings(ValidateConfigTool.Execute(ctx));

        warnings.ShouldNotContain(w => w.Contains("AppliesTo"));
    }

    [Fact]
    public void Validate_AppliesToNamingAKindFromTheOtherModel_Warns()
    {
        WriteModel("model.json", EntityModel);
        WriteModel("api.model.json", ApiModel);
        WriteTemplate("client.liquid", "x");
        var ctx = Configure(new ProjectConfiguration
        {
            OutputDir = _outputDir,
            Templates = new Dictionary<string, TemplateConfig>
            {
                // 'Class' is an entity Kind, not an API one.
                ["api-client"] = new()
                {
                    Path = "templates/client.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = "Class",
                    Model = "api.model.json"
                }
            }
        });

        var warnings = Warnings(ValidateConfigTool.Execute(ctx));

        warnings.ShouldContain(w => w.Contains("AppliesTo") && w.Contains("api.model.json"));
    }

    [Fact]
    public void Validate_MissingNamedModel_Warns()
    {
        WriteModel("model.json", EntityModel);
        WriteTemplate("client.liquid", "x");
        var ctx = Configure(new ProjectConfiguration
        {
            OutputDir = _outputDir,
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["api-client"] = new()
                {
                    Path = "templates/client.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    Model = "api.model.json"
                }
            }
        });

        Warnings(ValidateConfigTool.Execute(ctx))
            .ShouldContain(w => w.Contains("api.model.json") && w.Contains("does not exist"));
    }

    [Fact]
    public void Validate_ScopedOverrideIsCheckedAgainstThatArtifactsModel()
    {
        // Product/Price exists in the entity model. Scoped to api-client it can never apply,
        // and saying so is the whole point of the orphaned-override check.
        var ctx = TwoModelProject([
            new OverrideConfig { Path = "Product/Price", Artifact = "api-client", Ignore = true }
        ]);

        Warnings(ValidateConfigTool.Execute(ctx))
            .ShouldContain(w => w.Contains("Product/Price") && w.Contains("api.model.json"));
    }

    [Fact]
    public void Validate_UnscopedOverrideMatchingEitherModel_IsAccepted()
    {
        // An override with no Artifact applies to every template, so matching in one model is
        // enough — searching only model.json would report a false typo.
        var ctx = TwoModelProject([
            new OverrideConfig { Path = "Orders/List", Metadata = new Dictionary<string, object?> { ["Auth"] = true } }
        ]);

        Warnings(ValidateConfigTool.Execute(ctx))
            .ShouldNotContain(w => w.Contains("Orders/List"));
    }

    [Fact]
    public void Validate_UnscopedOverrideMatchingNoModel_NamesEveryModelSearched()
    {
        var ctx = TwoModelProject([
            new OverrideConfig { Path = "Nowhere/AtAll", Ignore = true }
        ]);

        var warning = Warnings(ValidateConfigTool.Execute(ctx))
            .ShouldHaveSingleItem();

        warning.ShouldContain("Nowhere/AtAll");
        warning.ShouldContain("model.json");
        warning.ShouldContain("api.model.json");
    }

    private static List<string> Warnings(string validateResult) =>
        JsonDocument.Parse(validateResult).RootElement
            .GetProperty("Warnings").EnumerateArray()
            .Select(w => w.GetString()!)
            .ToList();
}
