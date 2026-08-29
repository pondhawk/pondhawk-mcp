using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;
using Fluid.Values;
using Shouldly;

namespace Pondhawk.Generation.Tests.Rendering;

public class DispatchTagTests
{
    private readonly TemplateEngine _engine = new();

    private string Render(string source, Node node, string artifact = "entity", string variable = "item")
    {
        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        ctx.SetValue(variable, FluidValue.Create(node, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = artifact;
        return _engine.Render(template, ctx).Trim();
    }

    [Fact]
    public void Dispatch_CallsVariantMacroForKind()
    {
        var node = new Node { Name = "Orders", Kind = "Class" };
        node.SetVariant("entity", "SoftDelete");

        Render("""
            {%- macro DefaultClass(c) %}DEFAULT:{{ c.Name }}{%- endmacro %}
            {%- macro SoftDeleteClass(c) %}SOFTDELETE:{{ c.Name }}{%- endmacro %}
            {% dispatch item %}
            """, node).ShouldBe("SOFTDELETE:Orders");
    }

    [Fact]
    public void Dispatch_FallsBackToDefaultForKind()
    {
        Render("""
            {%- macro DefaultClass(c) %}DEFAULT:{{ c.Name }}{%- endmacro %}
            {% dispatch item %}
            """, new Node { Name = "Products", Kind = "Class" }).ShouldBe("DEFAULT:Products");
    }

    [Fact]
    public void Dispatch_FallsBackWhenVariantMacroIsMissing()
    {
        // An override naming a macro that has not been written yet degrades to the
        // default rather than breaking generation.
        var node = new Node { Name = "Price", Kind = "Property" };
        node.SetVariant("entity", "Currency");

        Render("""
            {%- macro DefaultProperty(p) %}DEFAULT:{{ p.Name }}{%- endmacro %}
            {% dispatch item %}
            """, node).ShouldBe("DEFAULT:Price");
    }

    [Fact]
    public void Dispatch_KindDrivesMacroName_NotTheCsharpType()
    {
        // The same Node type dispatches to a different macro purely because Kind differs,
        // which is what lets one engine serve arbitrary artifact shapes.
        var source = """
            {%- macro DefaultOperation(o) %}OP:{{ o.Name }}{%- endmacro %}
            {%- macro DefaultParameter(p) %}PARAM:{{ p.Name }}{%- endmacro %}
            {% dispatch item %}
            """;

        Render(source, new Node { Name = "Submit", Kind = "Operation" }).ShouldBe("OP:Submit");
        Render(source, new Node { Name = "CustomerId", Kind = "Parameter" }).ShouldBe("PARAM:CustomerId");
    }

    [Fact]
    public void Dispatch_NestedChildrenDispatchByTheirOwnKind()
    {
        var node = new Node
        {
            Name = "Orders", Kind = "Resource",
            Children =
            [
                new Node
                {
                    Name = "Submit", Kind = "Operation",
                    Children = [new Node { Name = "CustomerId", Kind = "Parameter" }]
                }
            ]
        };

        Render("""
            {%- macro DefaultResource(r) %}R:{{ r.Name }}{%- endmacro %}
            {%- macro DefaultOperation(o) %}O:{{ o.Name }}{%- endmacro %}
            {%- macro DefaultParameter(p) %}P:{{ p.Name }}{%- endmacro %}
            {%- dispatch item %}
            {%- for o in item.Children %}{% dispatch o %}
            {%- for p in o.Children %}{% dispatch p %}{% endfor %}
            {%- endfor %}
            """, node).ShouldBe("R:OrdersO:SubmitP:CustomerId");
    }

    [Fact]
    public void Dispatch_VariantIsScopedToArtifact()
    {
        var node = new Node { Name = "Price", Kind = "Property" };
        node.SetVariant("entity", "Currency");

        var source = """
            {%- macro DefaultProperty(p) %}DEFAULT:{{ p.Name }}{%- endmacro %}
            {%- macro CurrencyProperty(p) %}CURRENCY:{{ p.Name }}{%- endmacro %}
            {% dispatch item %}
            """;

        Render(source, node, artifact: "entity").ShouldBe("CURRENCY:Price");
        Render(source, node, artifact: "dto").ShouldBe("DEFAULT:Price");
    }

    [Fact]
    public void Dispatch_MissingMacroEntirely_EmitsAnInlineError()
    {
        Render("{% dispatch item %}", new Node { Name = "Orders", Kind = "Class" })
            .ShouldContain("dispatch error");
    }

    [Fact]
    public void Dispatch_NonNodeValue_EmitsAnInlineError()
    {
        _engine.TryParse("{% dispatch thing %}", out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("thing", FluidValue.Create("just a string", ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        _engine.Render(template, ctx).ShouldContain("dispatch error");
    }
}
