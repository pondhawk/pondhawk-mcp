using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class GenerateToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public GenerateToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_generate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _outputDir = Path.Combine(_tempDir, "output");
        WriteModel();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private const string DefaultModel = """
        {
          "Name": "Catalog",
          "Nodes": [
            {
              "Name": "Products", "Kind": "Class",
              "Children": [
                { "Name": "Id",    "Kind": "Property", "Type": "int" },
                { "Name": "Name",  "Kind": "Property", "Type": "string" },
                { "Name": "Price", "Kind": "Property", "Type": "decimal" }
              ]
            },
            {
              "Name": "Categories", "Kind": "Class",
              "Children": [ { "Name": "Title", "Kind": "Property", "Type": "string" } ]
            },
            { "Name": "ProductSummary", "Kind": "View" }
          ]
        }
        """;

    private void WriteModel(string? json = null)
        => File.WriteAllText(Path.Combine(_tempDir, "model.json"), json ?? DefaultModel);

    private ServerContext CreateContext(
        string? templateContent = null,
        string scope = "PerItem",
        string mode = "Always",
        string? appliesTo = null,
        List<OverrideConfig>? overrides = null,
        string outputPattern = "{{ item.Name }}.cs")
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"),
            templateContent ?? "// Generated: {{ item.Name }}");

        var config = new ProjectConfiguration
        {
            OutputDir = _outputDir,
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
            Values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Namespace"] = "Catalog.Data"
            },
            Overrides = overrides ?? []
        };

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);
        return new ServerContext(_tempDir);
    }

    private string Output(string name) => Path.Combine(_outputDir, name);

    // --- basics ---

    [Fact]
    public void Generate_WritesOneFilePerNode()
    {
        GenerateTool.Execute(CreateContext());

        File.Exists(Output("Products.cs")).ShouldBeTrue();
        File.Exists(Output("Categories.cs")).ShouldBeTrue();
        File.ReadAllText(Output("Products.cs")).ShouldContain("// Generated: Products");
    }

    [Fact]
    public void Generate_ReturnsSummary()
    {
        var json = GenerateTool.Execute(CreateContext());
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("Summary").GetString().ShouldContain("created");
        doc.RootElement.GetProperty("FilesWritten").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public void Generate_WithoutModel_Throws()
    {
        File.Delete(Path.Combine(_tempDir, "model.json"));

        Should.Throw<InvalidOperationException>(() => GenerateTool.Execute(CreateContext()))
            .Message.ShouldContain("model.json");
    }

    [Fact]
    public void Generate_MalformedTemplate_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            GenerateTool.Execute(CreateContext("{% for x in %}")));
    }

    // --- context bindings ---

    [Fact]
    public void Generate_BindsValuesAndModel()
    {
        GenerateTool.Execute(CreateContext("{{ values.Namespace }}|{{ model.Name }}|{{ item.Name }}"));

        File.ReadAllText(Output("Products.cs")).ShouldBe("Catalog.Data|Catalog|Products");
    }

    [Fact]
    public void Generate_BindsChildrenAndMetadata()
    {
        GenerateTool.Execute(CreateContext("{% for p in item.Children %}{{ p.Name }}:{{ p.Type }};{% endfor %}"));

        File.ReadAllText(Output("Products.cs")).ShouldBe("Id:int;Name:string;Price:decimal;");
    }

    [Fact]
    public void Generate_BindsParameters()
    {
        GenerateTool.Execute(
            CreateContext("{{ parameters.Stamp }}"),
            parameters: new Dictionary<string, object> { ["Stamp"] = "2026" });

        File.ReadAllText(Output("Products.cs")).ShouldBe("2026");
    }

    // --- scope ---

    [Fact]
    public void Generate_SingleScope_WritesOneFileForAllNodes()
    {
        GenerateTool.Execute(CreateContext(
            "{% for i in items %}{{ i.Name }},{% endfor %}",
            scope: "Single",
            outputPattern: "All.cs"));

        File.ReadAllText(Output("All.cs")).ShouldBe("Products,Categories,ProductSummary,");
    }

    // --- AppliesTo ---

    [Fact]
    public void Generate_AppliesTo_RestrictsToOneKind()
    {
        GenerateTool.Execute(CreateContext(appliesTo: "Class"));

        File.Exists(Output("Products.cs")).ShouldBeTrue();
        File.Exists(Output("ProductSummary.cs")).ShouldBeFalse();
    }

    [Fact]
    public void Generate_AppliesToAll_MatchesEveryKind()
    {
        GenerateTool.Execute(CreateContext(appliesTo: "All"));

        File.Exists(Output("ProductSummary.cs")).ShouldBeTrue();
    }

    [Fact]
    public void Generate_AppliesToUnknownKind_WritesNothing()
    {
        GenerateTool.Execute(CreateContext(appliesTo: "Endpoint"));

        Directory.Exists(_outputDir).ShouldBeFalse();
    }

    // --- filters ---

    [Fact]
    public void Generate_ItemsFilter_RestrictsToNamedNodes()
    {
        GenerateTool.Execute(CreateContext(), items: ["Products"]);

        File.Exists(Output("Products.cs")).ShouldBeTrue();
        File.Exists(Output("Categories.cs")).ShouldBeFalse();
    }

    [Fact]
    public void Generate_TemplatesFilter_RunsOnlyNamedTemplates()
    {
        var ctx = CreateContext();
        GenerateTool.Execute(ctx, templates: ["nosuchtemplate"]);

        Directory.Exists(_outputDir).ShouldBeFalse();
    }

    // --- modes ---

    [Fact]
    public void Generate_Always_OverwritesExisting()
    {
        GenerateTool.Execute(CreateContext("first"));
        File.ReadAllText(Output("Products.cs")).ShouldBe("first");

        GenerateTool.Execute(CreateContext("second"));
        File.ReadAllText(Output("Products.cs")).ShouldBe("second");
    }

    [Fact]
    public void Generate_SkipExisting_LeavesExistingAlone()
    {
        GenerateTool.Execute(CreateContext("first", mode: "SkipExisting"));
        GenerateTool.Execute(CreateContext("second", mode: "SkipExisting"));

        File.ReadAllText(Output("Products.cs")).ShouldBe("first");
    }

    [Fact]
    public void Generate_EmptyOutput_SkipsTheFile()
    {
        GenerateTool.Execute(CreateContext("{% if false %}x{% endif %}"));

        File.Exists(Output("Products.cs")).ShouldBeFalse();
    }

    // --- output patterns ---

    [Fact]
    public void Generate_OutputPatternSupportsFiltersAndSubdirectories()
    {
        GenerateTool.Execute(CreateContext(outputPattern: "Entities/{{ item.Name | singularize }}.g.cs"));

        File.Exists(Path.Combine(_outputDir, "Entities", "Product.g.cs")).ShouldBeTrue();
    }

    // --- overrides ---

    [Fact]
    public void Generate_OverrideVariant_SelectsTheVariantMacro()
    {
        var template = """
            {%- macro DefaultProperty(p) %}{{ p.Name }}:default;{%- endmacro %}
            {%- macro CurrencyProperty(p) %}{{ p.Name }}:money;{%- endmacro %}
            {%- for p in item.Children %}{% dispatch p %}{%- endfor %}
            """;

        GenerateTool.Execute(CreateContext(template, overrides:
            [new OverrideConfig { Path = "Products/Price", Artifact = "entity", Variant = "Currency" }]));

        var content = File.ReadAllText(Output("Products.cs"));
        content.ShouldContain("Price:money;");
        content.ShouldContain("Id:default;");
    }

    [Fact]
    public void Generate_OverrideIgnore_DropsTheNode()
    {
        GenerateTool.Execute(CreateContext(
            "{% for p in item.Children %}{{ p.Name }};{% endfor %}",
            overrides: [new OverrideConfig { Path = "Products/Price", Artifact = "entity", Ignore = true }]));

        File.ReadAllText(Output("Products.cs")).ShouldBe("Id;Name;");
    }

    [Fact]
    public void Generate_OverrideIgnoreOnRoot_SkipsTheWholeFile()
    {
        GenerateTool.Execute(CreateContext(
            overrides: [new OverrideConfig { Path = "Categories", Artifact = "entity", Ignore = true }]));

        File.Exists(Output("Products.cs")).ShouldBeTrue();
        File.Exists(Output("Categories.cs")).ShouldBeFalse();
    }

    [Fact]
    public void Generate_OverrideMetadata_ChangesRenderedValue()
    {
        GenerateTool.Execute(CreateContext(
            "{% for p in item.Children %}{{ p.Name }}:{{ p.Type }};{% endfor %}",
            overrides:
            [
                new OverrideConfig
                {
                    Path = "Products/Id", Artifact = "entity",
                    Metadata = new Dictionary<string, object?> { ["Type"] = "long" }
                }
            ]));

        File.ReadAllText(Output("Products.cs")).ShouldContain("Id:long;");
    }

    [Fact]
    public void Generate_OverridesDoNotMutateTheCachedModel()
    {
        // The cache hands back the same ModelFile across calls; if generation applied
        // overrides in place, a variant would persist into the next run.
        var ctx = CreateContext(
            "{% for p in item.Children %}{{ p.Name }};{% endfor %}",
            overrides: [new OverrideConfig { Path = "Products/Price", Artifact = "entity", Ignore = true }]);

        GenerateTool.Execute(ctx);
        File.ReadAllText(Output("Products.cs")).ShouldBe("Id;Name;");

        GenerateTool.Execute(ctx);
        File.ReadAllText(Output("Products.cs")).ShouldBe("Id;Name;");

        ctx.Cache.GetModel(ctx.ModelPath)!.Nodes[0].Children.Count.ShouldBe(3);
    }

    // --- nesting ---

    [Fact]
    public void Generate_HandlesThreeLevelModels()
    {
        WriteModel("""
            {
              "Name": "Api",
              "Nodes": [
                { "Name": "Orders", "Kind": "Resource", "Children": [
                  { "Name": "Submit", "Kind": "Operation", "Children": [
                    { "Name": "CustomerId", "Kind": "Parameter", "Type": "string" }
                  ]}
                ]}
              ]
            }
            """);

        GenerateTool.Execute(CreateContext("""
            {%- macro DefaultResource(r) %}R:{{ r.Name }}{%- endmacro %}
            {%- macro DefaultOperation(o) %}O:{{ o.Name }}{%- endmacro %}
            {%- macro DefaultParameter(p) %}P:{{ p.Name }}:{{ p.Type }}{%- endmacro %}
            {%- dispatch item %}
            {%- for o in item.Children %}{% dispatch o %}
            {%- for p in o.Children %}{% dispatch p %}{% endfor %}
            {%- endfor %}
            """));

        File.ReadAllText(Output("Orders.cs")).Trim()
            .ShouldBe("R:OrdersO:SubmitP:CustomerId:string");
    }

    [Fact]
    public void Generate_DeepOverridePathReachesNestedNodes()
    {
        WriteModel("""
            {
              "Nodes": [
                { "Name": "Orders", "Kind": "Resource", "Children": [
                  { "Name": "Submit", "Kind": "Operation", "Children": [
                    { "Name": "CustomerId", "Kind": "Parameter" },
                    { "Name": "Secret", "Kind": "Parameter" }
                  ]}
                ]}
              ]
            }
            """);

        GenerateTool.Execute(CreateContext(
            "{% for o in item.Children %}{% for p in o.Children %}{{ p.Name }};{% endfor %}{% endfor %}",
            overrides: [new OverrideConfig { Path = "**/Secret", Artifact = "entity", Ignore = true }]));

        File.ReadAllText(Output("Orders.cs")).ShouldBe("CustomerId;");
    }
}
