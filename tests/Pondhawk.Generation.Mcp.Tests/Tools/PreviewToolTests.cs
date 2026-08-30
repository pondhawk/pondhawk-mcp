using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

public class PreviewToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public PreviewToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_preview_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        _outputDir = Path.Combine(_tempDir, "output");

        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product", "Kind": "Class", "Children": [
                    { "Name": "Id",    "Kind": "Property", "Type": "int" },
                    { "Name": "Price", "Kind": "Property", "Type": "decimal" } ] },
                { "Name": "Category", "Kind": "Class", "Children": [
                    { "Name": "Title", "Kind": "Property", "Type": "string" } ] },
                { "Name": "Summary", "Kind": "View" }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private const string Body =
        "class {{ item.Name }} {\n{%- for c in item.Children %}\n{% dispatch c %}{%- endfor %}\n}";

    private const string Macros =
        "{%- macro DefaultProperty(p) %}  {{ p.Type }} {{ p.Name }};{%- endmacro %}";

    private ServerContext Configure(
        string template = Macros + Body,
        string scope = "PerItem",
        string mode = "Always",
        string? appliesTo = "Class",
        List<OverrideConfig>? overrides = null,
        string outputPattern = "{{ item.Name }}.cs")
    {
        File.WriteAllText(Path.Combine(_tempDir, "templates", "entity.liquid"), template);

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = outputPattern,
                    Scope = scope,
                    Mode = mode,
                    AppliesTo = appliesTo
                }
            },
            Overrides = overrides ?? []
        });

        return new ServerContext(_tempDir);
    }

    private static JsonElement Json(string r) => JsonDocument.Parse(r).RootElement;
    private static string Content(JsonElement r) => r.GetProperty("Content").GetString()!;

    // --- rendering ------------------------------------------------------------

    [Fact]
    public void RendersOneNodeAndWritesNothing()
    {
        var result = Json(PreviewTool.Execute(Configure(), "entity", "Product"));

        Content(result).ShouldContain("class Product {");
        Content(result).ShouldContain("int Id;");
        result.GetProperty("NothingWritten").GetBoolean().ShouldBeTrue();
        Directory.Exists(_outputDir).ShouldBeFalse();
    }

    [Fact]
    public void ReportsWhereTheContentWouldHaveGone()
    {
        var result = Json(PreviewTool.Execute(Configure(), "entity", "Product"));

        result.GetProperty("OutputPath").GetString().ShouldBe("Product.cs");
        result.GetProperty("Mode").GetString().ShouldBe("Always");
        result.GetProperty("Model").GetString().ShouldBe("model.json");
        result.GetProperty("Lines").GetInt32().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void WithoutANode_RendersTheFirstOneTheTemplateMatches()
    {
        // While authoring a macro the question is usually "show me any one of these".
        var result = Json(PreviewTool.Execute(Configure(), "entity"));

        result.GetProperty("Node").GetString().ShouldBe("Product");
    }

    [Fact]
    public void ASingleScopeTemplate_RendersEveryMatchingNodeAtOnce()
    {
        var result = Json(PreviewTool.Execute(
            Configure("{%- for i in items %}{{ i.Name }};{%- endfor %}", scope: "Single",
                outputPattern: "All.cs"), "entity"));

        Content(result).ShouldBe("Product;Category;");
    }

    [Fact]
    public void OverridesAndVariantsApplyExactlyAsInARealRun()
    {
        // A preview that skipped overrides would be wrong for precisely the nodes worth looking at.
        var result = Json(PreviewTool.Execute(
            Configure(
                Macros + "{%- macro CurrencyProperty(p) %}  money {{ p.Name }};{%- endmacro %}" + Body,
                overrides: [new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Currency" }]),
            "entity", "Product"));

        Content(result).ShouldContain("money Price;");
        Content(result).ShouldContain("int Id;");
    }

    [Fact]
    public void ParametersReachTheTemplate()
    {
        var result = Json(PreviewTool.Execute(
            Configure("{{ parameters.Flavour }}"), "entity", "Product",
            new Dictionary<string, object> { ["Flavour"] = "vanilla" }));

        Content(result).ShouldBe("vanilla");
    }

    [Fact]
    public void AnEmptyRender_SaysGenerateWouldSkipTheFile()
    {
        // The quiet failure again: without saying so, an empty preview looks like a tool bug.
        var result = Json(PreviewTool.Execute(Configure("{% if false %}x{% endif %}"), "entity", "Product"));

        result.GetProperty("Note").GetString()!.ShouldContain("would skip the file");
    }

    // --- errors come back as content, not as failures -------------------------

    [Fact]
    public void ABrokenTemplate_ReturnsTheErrorRatherThanThrowing()
    {
        // A half-finished macro is the normal state while writing one.
        var result = Json(PreviewTool.Execute(Configure("{% for x in %}"), "entity", "Product"));

        result.GetProperty("Error").GetString()!.ShouldContain("entity");
        result.GetProperty("NothingWritten").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void AnUnknownTemplate_ListsTheOnesThatExist()
    {
        var result = Json(PreviewTool.Execute(Configure(), "nope"));

        result.GetProperty("Error").GetString()!.ShouldContain("Available: entity");
    }

    [Fact]
    public void AnUnknownNode_ListsWhatTheModelActuallyHas()
    {
        var result = Json(PreviewTool.Execute(Configure(), "entity", "Nonexistent"));

        var error = result.GetProperty("Error").GetString()!;
        error.ShouldContain("No top-level node named 'Nonexistent'");
        error.ShouldContain("Product");
    }

    [Fact]
    public void ATemplateMatchingNoKind_SaysWhichKindsArePresent()
    {
        var error = Json(PreviewTool.Execute(Configure(appliesTo: "Enum"), "entity"))
            .GetProperty("Error").GetString()!;

        error.ShouldContain("applies to Kind 'Enum'");
        error.ShouldContain("present: Class, View");
    }

    [Fact]
    public void ANodeRemovedByAnIgnoreOverride_SaysSo()
    {
        var result = Json(PreviewTool.Execute(
            Configure(overrides: [new OverrideConfig { Path = "Product", Artifact = "entity", Ignore = true }]),
            "entity", "Product"));

        result.GetProperty("Error").GetString()!.ShouldContain("Ignore override");
    }

    [Fact]
    public void AMissingModel_ReturnsTheErrorRatherThanThrowing()
    {
        File.Delete(Path.Combine(_tempDir, "model.json"));

        Json(PreviewTool.Execute(Configure(), "entity", "Product"))
            .GetProperty("Error").GetString()!.ShouldContain("model.json not found");
    }

    // --- consistency with the real run ---------------------------------------

    [Fact]
    public void PreviewMatchesWhatGenerateThenWrites()
    {
        // Preview plans through the same code a run does; this pins that they cannot drift.
        var ctx = Configure();
        var previewed = Content(Json(PreviewTool.Execute(ctx, "entity", "Product")));

        GenerateTool.Execute(ctx);

        File.ReadAllText(Path.Combine(_outputDir, "Product.cs")).ShouldBe(previewed);
    }
}
