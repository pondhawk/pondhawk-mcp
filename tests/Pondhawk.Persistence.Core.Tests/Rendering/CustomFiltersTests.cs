using Pondhawk.Persistence.Core.Rendering;
using Fluid.Values;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Rendering;

public class CustomFiltersTests
{
    private readonly TemplateEngine _engine = new();

    private string Render(string template)
    {
        _engine.TryParse(template, out var tmpl, out var error).ShouldBeTrue(error);
        var ctx = _engine.CreateContext();
        return _engine.Render(tmpl, ctx).Trim();
    }

    [Theory]
    [InlineData("order_item", "OrderItem")]
    [InlineData("OrderItem", "OrderItem")]
    [InlineData("order_items", "OrderItems")]
    [InlineData("order", "Order")]
    public void PascalCase_ConvertsCorrectly(string input, string expected)
    {
        Render($"{{{{ \"{input}\" | pascal_case }}}}").ShouldBe(expected);
    }

    [Theory]
    [InlineData("OrderItem", "orderItem")]
    [InlineData("order_item", "orderItem")]
    [InlineData("Order", "order")]
    public void CamelCase_ConvertsCorrectly(string input, string expected)
    {
        Render($"{{{{ \"{input}\" | camel_case }}}}").ShouldBe(expected);
    }

    [Theory]
    [InlineData("OrderItem", "order_item")]
    [InlineData("order_item", "order_item")]
    [InlineData("Order", "order")]
    public void SnakeCase_ConvertsCorrectly(string input, string expected)
    {
        Render($"{{{{ \"{input}\" | snake_case }}}}").ShouldBe(expected);
    }

    [Theory]
    [InlineData("Category", "Categories")]
    [InlineData("Product", "Products")]
    [InlineData("Order", "Orders")]
    public void Pluralize_ConvertsCorrectly(string input, string expected)
    {
        Render($"{{{{ \"{input}\" | pluralize }}}}").ShouldBe(expected);
    }

    [Theory]
    [InlineData("Categories", "Category")]
    [InlineData("Products", "Product")]
    [InlineData("Orders", "Order")]
    public void Singularize_ConvertsCorrectly(string input, string expected)
    {
        Render($"{{{{ \"{input}\" | singularize }}}}").ShouldBe(expected);
    }

    [Fact]
    public void TypeNullable_ValueType_AppendsQuestionMark()
    {
        _engine.TryParse("{{ type | type_nullable: nullable }}", out var tmpl, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("type", new StringValue("int"));
        ctx.SetValue("nullable", BooleanValue.Create(true));
        _engine.Render(tmpl, ctx).Trim().ShouldBe("int?");
    }

    [Fact]
    public void TypeNullable_NotNullable_ReturnsUnchanged()
    {
        _engine.TryParse("{{ type | type_nullable: nullable }}", out var tmpl, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("type", new StringValue("int"));
        ctx.SetValue("nullable", BooleanValue.Create(false));
        _engine.Render(tmpl, ctx).Trim().ShouldBe("int");
    }

    [Fact]
    public void TypeNullable_ReferenceType_AppendsQuestionMark()
    {
        _engine.TryParse("{{ type | type_nullable: nullable }}", out var tmpl, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("type", new StringValue("string"));
        ctx.SetValue("nullable", BooleanValue.Create(true));
        _engine.Render(tmpl, ctx).Trim().ShouldBe("string?");
    }

    [Fact]
    public void EmptyString_Filters_ReturnEmpty()
    {
        Render("{{ \"\" | pascal_case }}").ShouldBe("");
        Render("{{ \"\" | camel_case }}").ShouldBe("");
        Render("{{ \"\" | snake_case }}").ShouldBe("");
    }
}
