using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;
using Fluid.Values;
using Shouldly;

namespace Pondhawk.Generation.Tests.Pipeline;

/// <summary>
/// End-to-end through the Core pieces generation actually composes:
/// load the model, apply overrides, render through dispatch, write the file.
/// </summary>
public class GeneratePipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateEngine _engine = new();

    public GeneratePipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_pipe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private const string ModelJson = """
        {
          "Name": "Catalog",
          "Nodes": [
            {
              "Name": "Product",
              "Kind": "Class",
              "Children": [
                { "Name": "Id",    "Kind": "Property", "Type": "int",     "IsNullable": false },
                { "Name": "Price", "Kind": "Property", "Type": "decimal", "IsNullable": false },
                { "Name": "Note",  "Kind": "Property", "Type": "string",  "IsNullable": true }
              ]
            },
            {
              "Name": "Category",
              "Kind": "Class",
              "Children": [ { "Name": "Id", "Kind": "Property", "Type": "int", "IsNullable": false } ]
            }
          ]
        }
        """;

    private const string EntityTemplate = """
        namespace {{ values.Namespace }};

        {%- macro DefaultClass(c) %}
        public partial class {{ c.Name | pascal_case }}
        {%- endmacro %}
        {%- macro DefaultProperty(p) %}
            public {{ p.Type | type_nullable: p.IsNullable }} {{ p.Name | pascal_case }} { get; set; }
        {%- endmacro %}
        {%- macro CurrencyProperty(p) %}
            public decimal {{ p.Name | pascal_case }} { get; set; } // money
        {%- endmacro %}

        {% dispatch item %}
        {
        {%- for p in item.Children %}
        {% dispatch p %}
        {%- endfor %}
        }
        """;

    private string Generate(string artifact, List<OverrideConfig> overrides, out ModelFile model)
    {
        model = ModelFileLoader.Deserialize(ModelJson);
        var resolved = OverrideResolver.Apply(
            model.Nodes.Select(n => n.Clone()).ToList(), artifact, overrides);

        _engine.TryParse(EntityTemplate, out var template, out var error).ShouldBeTrue(error);

        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(resolved[0], ctx.Options));
        ctx.SetValue("values", FluidValue.Create(
            new Dictionary<string, object?> { ["Namespace"] = "Catalog.Data" }, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = artifact;

        return _engine.Render(template, ctx);
    }

    [Fact]
    public void ModelToRenderedFile()
    {
        var content = Generate("entity", [], out _);

        content.ShouldContain("namespace Catalog.Data;");
        content.ShouldContain("public partial class Product");
        content.ShouldContain("public int Id { get; set; }");
        content.ShouldContain("public decimal Price { get; set; }");
        content.ShouldContain("public string? Note { get; set; }");
    }

    [Fact]
    public void OverrideVariant_ChangesOneNodeOnly()
    {
        var content = Generate("entity",
            [new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Currency" }], out _);

        content.ShouldContain("public decimal Price { get; set; } // money");
        content.ShouldContain("public int Id { get; set; }");
    }

    [Fact]
    public void OverrideIgnore_DropsNodeFromOutput()
    {
        var content = Generate("entity",
            [new OverrideConfig { Path = "Product/Note", Artifact = "entity", Ignore = true }], out _);

        content.ShouldNotContain("Note");
        content.ShouldContain("Price");
    }

    [Fact]
    public void OverrideMetadata_ChangesRenderedType()
    {
        var content = Generate("entity",
        [
            new OverrideConfig
            {
                Path = "Product/Id", Artifact = "entity",
                Metadata = new Dictionary<string, object?> { ["Type"] = "long" }
            }
        ], out _);

        content.ShouldContain("public long Id { get; set; }");
    }

    [Fact]
    public void OverridesDoNotLeakBetweenArtifacts()
    {
        // Generation clones before applying overrides; without that, a variant set for one
        // template would still be set when the next template rendered the same node.
        var overrides = new List<OverrideConfig>
        {
            new() { Path = "Product/Price", Artifact = "entity", Variant = "Currency" }
        };

        Generate("entity", overrides, out var model).ShouldContain("// money");
        Generate("dto", overrides, out _).ShouldNotContain("// money");

        // And the source model itself is untouched.
        model.Nodes[0].Children.Single(c => c.Name == "Price").GetVariant("entity").ShouldBe("");
    }

    [Fact]
    public void RenderedContentWritesThroughFileWriter()
    {
        var content = Generate("entity", [], out _);
        const string name = "Product.generated.cs";
        var path = Path.Combine(_tempDir, name);

        FileWriter.WriteFile(_tempDir, name, content, "Always").Action.ShouldBe("Created");
        File.ReadAllText(path).ShouldContain("public partial class Product");

        FileWriter.WriteFile(_tempDir, name, content, "Always").Action.ShouldBe("Overwritten");
        FileWriter.WriteFile(_tempDir, name, content, "SkipExisting").Action.ShouldBe("SkippedExisting");
    }

    [Fact]
    public void EveryNodeOfAKindRendersThroughOneMacro()
    {
        // The consistency guarantee: change DefaultProperty and every property in every
        // artifact changes with it.
        var content = Generate("entity", [], out _);
        var propertyLines = content.Split('\n').Where(l => l.Contains("{ get; set; }")).ToList();

        propertyLines.Count.ShouldBe(3);
        propertyLines.ShouldAllBe(l => l.StartsWith("    public "));
    }

    [Fact]
    public void ModelRoundTripsThroughDisk()
    {
        var path = Path.Combine(_tempDir, "model.json");
        File.WriteAllText(path, ModelJson);

        var loaded = ModelFileLoader.Load(path);
        loaded.Name.ShouldBe("Catalog");
        loaded.Nodes.Count.ShouldBe(2);
        loaded.Nodes[0].Children.Count.ShouldBe(3);
    }
}
