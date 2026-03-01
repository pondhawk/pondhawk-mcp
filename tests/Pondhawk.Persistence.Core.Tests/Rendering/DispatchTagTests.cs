using Pondhawk.Persistence.Core.Models;
using Pondhawk.Persistence.Core.Rendering;
using Fluid.Values;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Rendering;

public class DispatchTagTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void Dispatch_Model_CallsVariantClassMacro()
    {
        var source = """
            {%- macro DefaultClass(m) %}DEFAULT:{{ m.Name }}{%- endmacro %}
            {%- macro SoftDeleteClass(m) %}SOFTDELETE:{{ m.Name }}{%- endmacro %}
            {% dispatch entity %}
            """;

        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        var model = new Model { Name = "Orders" };
        model.SetVariant("entity", "SoftDelete");
        ctx.SetValue("entity", FluidValue.Create(model, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("SOFTDELETE:Orders");
    }

    [Fact]
    public void Dispatch_Model_FallsBackToDefaultClass()
    {
        var source = """
            {%- macro DefaultClass(m) %}DEFAULT:{{ m.Name }}{%- endmacro %}
            {% dispatch entity %}
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("entity", FluidValue.Create(new Model { Name = "Products" }, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("DEFAULT:Products");
    }

    [Fact]
    public void Dispatch_Attribute_CallsVariantPropertyMacro()
    {
        var source = """
            {%- macro DefaultProperty(a) %}DEFAULT:{{ a.Name }}{%- endmacro %}
            {%- macro CurrencyProperty(a) %}CURRENCY:{{ a.Name }}{%- endmacro %}
            {% dispatch attr %}
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        var attr = new Attribute { Name = "Price", ClrType = "decimal" };
        attr.SetVariant("entity", "Currency");
        ctx.SetValue("attr", FluidValue.Create(attr, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("CURRENCY:Price");
    }

    [Fact]
    public void Dispatch_Attribute_FallsBackToDefaultProperty()
    {
        var source = """
            {%- macro DefaultProperty(a) %}DEFAULT:{{ a.Name }}{%- endmacro %}
            {% dispatch attr %}
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("attr", FluidValue.Create(new Attribute { Name = "Id", ClrType = "int" }, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("DEFAULT:Id");
    }

    [Fact]
    public void Dispatch_MacroNotFound_WritesErrorComment()
    {
        var source = "{% dispatch entity %}";

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        var model = new Model { Name = "Orders" };
        model.SetVariant("entity", "Missing");
        ctx.SetValue("entity", FluidValue.Create(model, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("dispatch error");
        result.ShouldContain("MissingClass");
    }

    [Fact]
    public void Dispatch_View_BehavesLikeTable()
    {
        var source = """
            {%- macro DefaultClass(m) %}CLASS:{{ m.Name }}:{{ m.IsView }}{%- endmacro %}
            {% dispatch entity %}
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("entity", FluidValue.Create(new Model { Name = "ActiveProducts", IsView = true }, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("CLASS:ActiveProducts");
    }

    [Fact]
    public void Dispatch_InsideLoop_SingleFile()
    {
        var source = """
            {%- macro DefaultClass(m) %}{{ m.Name }}{%- endmacro %}
            {%- for e in entities %}
            {% dispatch e %}
            {%- endfor %}
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        var entities = new List<Model>
        {
            new() { Name = "Products" },
            new() { Name = "Orders" }
        };
        ctx.SetValue("entities", FluidValue.Create(entities, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "dbcontext";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("Products");
        result.ShouldContain("Orders");
    }

    [Fact]
    public void Dispatch_ArtifactNameFromContext()
    {
        var source = """
            {%- macro DefaultProperty(a) %}DEFAULT{%- endmacro %}
            {%- macro SpecialProperty(a) %}SPECIAL{%- endmacro %}
            {% dispatch attr %}
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        var attr = new Attribute { Name = "Price" };
        attr.SetVariant("dto", "Special");
        ctx.SetValue("attr", FluidValue.Create(attr, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "dto";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("SPECIAL");
    }
}
