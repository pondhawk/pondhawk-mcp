using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;
using Fluid.Values;
using Shouldly;

namespace Pondhawk.Generation.Tests.Rendering;

public class TemplateRenderingTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void PerItem_RendersWithItemAndValues()
    {
        var source = "namespace {{ values.Namespace }};class {{ item.Name }}{}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(new Node { Name = "Products", Kind = "Class" }, ctx.Options));
        ctx.SetValue("values", FluidValue.Create(
            new Dictionary<string, object?> { ["Namespace"] = "MyApp.Data" }, ctx.Options));

        var result = _engine.Render(template, ctx);
        result.ShouldContain("MyApp.Data");
        result.ShouldContain("Products");
    }

    [Fact]
    public void Single_RendersWithItemsCollection()
    {
        _engine.TryParse("{% for i in items %}{{ i.Name }},{% endfor %}", out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("items", FluidValue.Create(new List<Node>
        {
            new() { Name = "Products", Kind = "Class" },
            new() { Name = "Categories", Kind = "Class" }
        }, ctx.Options));

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("Products,Categories,");
    }

    [Fact]
    public void Metadata_IsReachableAsADirectMember()
    {
        // The point of the dynamic accessor: templates read model metadata without the
        // engine knowing the model's shape, and without a Metadata. prefix.
        var node = new Node { Name = "Price", Kind = "Property" };
        node.Metadata["Type"] = "decimal";
        node.Metadata["IsNullable"] = true;

        _engine.TryParse("{{ item.Type | type_nullable: item.IsNullable }}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(node, ctx.Options));

        _engine.Render(template, ctx).Trim().ShouldBe("decimal?");
    }

    [Fact]
    public void Metadata_AbsentKeyRendersEmptyRatherThanThrowing()
    {
        _engine.TryParse("[{{ item.NoSuchKey }}]", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(new Node { Name = "Id", Kind = "Property" }, ctx.Options));

        _engine.Render(template, ctx).Trim().ShouldBe("[]");
    }

    [Fact]
    public void Metadata_AbsentKeyIsFalsyForConditionals()
    {
        _engine.TryParse("{% if item.IsKey %}KEY{% else %}NOT{% endif %}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(new Node { Name = "Name", Kind = "Property" }, ctx.Options));

        _engine.Render(template, ctx).Trim().ShouldBe("NOT");
    }

    [Fact]
    public void Model_RootIsAvailable()
    {
        var model = ModelFileLoader.Deserialize("""
            { "Name": "Catalog", "Version": "2.1", "Nodes": [] }
            """);

        _engine.TryParse("{{ model.Name }}-{{ model.Version }}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("model", FluidValue.Create(model, ctx.Options));

        _engine.Render(template, ctx).Trim().ShouldBe("Catalog-2.1");
    }

    [Fact]
    public void Config_IsAvailable()
    {
        _engine.TryParse("{{ config.OutputDir }}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("config", FluidValue.Create(
            new ProjectConfiguration { OutputDir = "src/Generated" }, ctx.Options));

        _engine.Render(template, ctx).Trim().ShouldBe("src/Generated");
    }

    [Fact]
    public void UndefinedVariable_Throws()
    {
        _engine.TryParse("{{ nosuchvariable.Name }}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();

        Should.Throw<InvalidOperationException>(() => _engine.Render(template, ctx));
    }

    [Fact]
    public void ChildrenCollection_Iterates()
    {
        var node = new Node
        {
            Name = "Product", Kind = "Class",
            Children = [new Node { Name = "Id", Kind = "Property" }, new Node { Name = "Price", Kind = "Property" }]
        };

        _engine.TryParse("{% for c in item.Children %}{{ c.Name }};{% endfor %}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(node, ctx.Options));

        _engine.Render(template, ctx).Trim().ShouldBe("Id;Price;");
    }
}
