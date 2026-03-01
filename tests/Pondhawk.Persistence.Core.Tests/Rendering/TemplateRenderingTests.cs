using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Models;
using Pondhawk.Persistence.Core.Rendering;
using Fluid.Values;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Rendering;

public class TemplateRenderingTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void PerModel_RendersWithEntityContext()
    {
        var source = "namespace {{ config.Defaults.Namespace }};class {{ entity.Name }}{}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("entity", FluidValue.Create(new Model { Name = "Products", Schema = "dbo" }, ctx.Options));
        ctx.SetValue("config", FluidValue.Create(new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Namespace = "MyApp.Data" }
        }, ctx.Options));

        var result = _engine.Render(template, ctx);
        result.ShouldContain("MyApp.Data");
        result.ShouldContain("Products");
    }

    [Fact]
    public void SingleFile_RendersWithEntitiesContext()
    {
        var source = "{% for e in entities %}{{ e.Name }},{% endfor %}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("entities", FluidValue.Create(new List<Model>
        {
            new() { Name = "Products" },
            new() { Name = "Categories" }
        }, ctx.Options));

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("Products");
        result.ShouldContain("Categories");
    }

    [Fact]
    public void DatabaseContext_Available()
    {
        var source = "{{ database.Database }}-{{ database.Provider }}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("database", FluidValue.Create(new { Database = "Inventory", Provider = "sqlserver" }, ctx.Options));

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("Inventory-sqlserver");
    }

    [Fact]
    public void Parameters_PassThrough()
    {
        var source = "{{ parameters.custom_flag }}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("parameters", FluidValue.Create(new Dictionary<string, object> { ["custom_flag"] = "yes" }, ctx.Options));

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("yes");
    }

    [Fact]
    public void CompleteEntityTemplate_ProducesValidOutput()
    {
        var source = """
            namespace {{ config.Defaults.Namespace }}.Entities;

            {%- macro DefaultClass(m) %}
            public partial class {{ m.Name | pascal_case }}
            {%- endmacro %}

            {% dispatch entity %}
            {

            {%- macro DefaultProperty(a) %}
                public {{ a.ClrType | type_nullable: a.IsNullable }} {{ a.Name | pascal_case }} { get; set; }
            {%- endmacro %}

            {%- for a in entity.Attributes %}
            {% dispatch a %}
            {%- endfor %}
            }
            """;

        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var model = new Model { Name = "Products", Schema = "dbo" };
        model.Attributes.Add(new Attribute { Name = "Id", ClrType = "int", IsNullable = false });
        model.Attributes.Add(new Attribute { Name = "Name", ClrType = "string", IsNullable = false });
        model.Attributes.Add(new Attribute { Name = "Price", ClrType = "decimal", IsNullable = true });

        var ctx = _engine.CreateContext();
        ctx.SetValue("entity", FluidValue.Create(model, ctx.Options));
        ctx.SetValue("config", FluidValue.Create(new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Namespace = "MyApp.Data" }
        }, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx);
        result.ShouldContain("namespace MyApp.Data.Entities;");
        result.ShouldContain("public partial class Products");
        result.ShouldContain("public int Id { get; set; }");
        result.ShouldContain("public string Name { get; set; }");
        result.ShouldContain("public decimal? Price { get; set; }");
    }

    [Fact]
    public void TryParse_InvalidTemplate_ReturnsFalse()
    {
        var success = _engine.TryParse("{% if %}", out _, out var error);
        success.ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void SingleFile_AccessesViewsAndSchemas()
    {
        var source = "{% for s in schemas %}{{ s.Name }}:{% for t in s.Tables %}{{ t.Name }},{% endfor %}{% endfor %}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("schemas", FluidValue.Create(new[]
        {
            new { Name = "dbo", Tables = new[] { new { Name = "Products" }, new { Name = "Orders" } } }
        }, ctx.Options));

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldContain("dbo");
        result.ShouldContain("Products");
        result.ShouldContain("Orders");
    }

    [Fact]
    public void ValidateFilterNames_KnownFilters_ReturnsEmpty()
    {
        var source = "{{ entity.Name | pascal_case }} {{ x | camel_case }} {{ y | type_nullable: true }}";
        var unknown = TemplateEngine.ValidateFilterNames(source);
        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateFilterNames_UnknownFilter_ReturnsFilterName()
    {
        var source = "{{ entity.Name | bogus_filter }}";
        var unknown = TemplateEngine.ValidateFilterNames(source);
        unknown.Count.ShouldBe(1);
        unknown[0].ShouldBe("bogus_filter");
    }

    [Fact]
    public void ValidateFilterNames_DuplicateUnknown_Deduplicated()
    {
        var source = "{{ a | bad }} {{ b | bad }}";
        var unknown = TemplateEngine.ValidateFilterNames(source);
        unknown.Count.ShouldBe(1);
    }

    [Fact]
    public void StrictVariables_UndefinedVariable_Throws()
    {
        var source = "{{ missing_var }}";
        _engine.TryParse(source, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        Should.Throw<InvalidOperationException>(() => _engine.Render(template, ctx));
    }
}
