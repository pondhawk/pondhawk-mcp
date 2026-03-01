using Pondhawk.Persistence.Core.Rendering;
using Fluid.Values;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Rendering;

public class MacroTagTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void Macro_DefinedAndCalled_RendersOutput()
    {
        var source = """
            {%- macro Greet(name) %}Hello {{ name }}{%- endmacro %}
            {{ Greet("World") }}
            """;

        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("Hello World");
    }

    [Fact]
    public void Macro_MultipleParameters_AllAccessible()
    {
        var source = """
            {%- macro Format(a, b) %}{{ a }}-{{ b }}{%- endmacro %}
            {{ Format("X", "Y") }}
            """;

        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("X-Y");
    }

    [Fact]
    public void Macro_AcceptsObjectParameter()
    {
        var source = """
            {%- macro ShowName(obj) %}Name:{{ obj.Name }}{%- endmacro %}
            {{ ShowName(item) }}
            """;

        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        ctx.SetValue("item", FluidValue.Create(new { Name = "TestItem" }, ctx.Options));
        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("Name:TestItem");
    }

    [Fact]
    public void Macro_MultipleMacros_IndependentlyCallable()
    {
        var source = """
            {%- macro First(x) %}FIRST:{{ x }}{%- endmacro %}
            {%- macro Second(x) %}SECOND:{{ x }}{%- endmacro %}
            {{ First("A") }}|{{ Second("B") }}
            """;

        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("FIRST:A");
        result.ShouldContain("SECOND:B");
    }

    [Fact]
    public void Macro_WithFilters_WorksCorrectly()
    {
        var source = """
            {%- macro TypedProp(name, type) %}public {{ type }} {{ name | pascal_case }} { get; set; }{%- endmacro %}
            {{ TypedProp("order_item", "string") }}
            """;

        _engine.TryParse(source, out var template, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("public string OrderItem { get; set; }");
    }
}
