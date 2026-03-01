using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Pondhawk.Persistence.Core.Rendering;
using Pondhawk.Persistence.Core.Tests.Fixtures;
using Fluid;
using Fluid.Values;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Pipeline;

public class GeneratePipelineTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly TemplateEngine _engine = new();
    private readonly string _tempDir;

    public GeneratePipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_pipeline_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _db = new SqliteTestDatabase()
            .AddTable("Categories", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Description TEXT")
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL, CategoryId INTEGER REFERENCES Categories(Id)")
            .AddTable("Orders", "Id INTEGER PRIMARY KEY, OrderDate TEXT NOT NULL, CustomerId INTEGER, Total REAL")
            .AddView("ActiveProducts", "SELECT Id, Name, Price FROM Products WHERE Price > 0")
            .Build();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private List<Model> IntrospectAndProcess(ProjectConfiguration config, bool includeViews = false)
    {
        var models = SchemaIntrospector.Introspect(
            _db.Connection, "sqlite", config.Defaults,
            includeViews: includeViews);

        var typeMapper = new TypeMapper("sqlite", config.TypeMappings, config.DataTypes);
        foreach (var m in models)
            foreach (var a in m.Attributes)
                typeMapper.ApplyMapping(a);

        RelationshipMerger.Merge(models, config.Relationships, config.Defaults.Schema);
        return models;
    }

    [Fact]
    public void FullPipeline_PerModel_ProducesCorrectOutput()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Namespace = "TestApp.Data", Schema = "main" }
        };

        var models = IntrospectAndProcess(config);
        models.Count.ShouldBeGreaterThanOrEqualTo(3); // Categories, Products, Orders

        var templateSource = """
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

            {%- for fk in entity.ForeignKeys %}
                public virtual {{ fk.PrincipalTable | pascal_case | singularize }} {{ fk.PrincipalTable | pascal_case | singularize }} { get; set; } = null!;
            {%- endfor %}
            }
            """;

        _engine.TryParse(templateSource, out var template, out var error).ShouldBeTrue(error);

        var products = models.First(m => m.Name == "Products");
        var ctx = _engine.CreateContext();
        ctx.SetValue("entity", FluidValue.Create(products, ctx.Options));
        ctx.SetValue("config", FluidValue.Create(config, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx);

        result.ShouldContain("namespace TestApp.Data.Entities;");
        result.ShouldContain("public partial class Products");
        // SQLite columns may report as nullable; check type and property name
        result.ShouldContain("Id { get; set; }");
        result.ShouldContain("Name { get; set; }");
        result.ShouldContain("public virtual Category Category { get; set; } = null!;");
    }

    [Fact]
    public void FullPipeline_SingleFile_RendersAllEntities()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Namespace = "TestApp.Data", ContextName = "TestApp", Schema = "main" }
        };

        var models = IntrospectAndProcess(config);

        var templateSource = """
            namespace {{ config.Defaults.Namespace }};

            public partial class {{ config.Defaults.ContextName }}DbContext
            {
            {%- for e in entities %}
                public DbSet<{{ e.Name | pascal_case }}> {{ e.Name | pascal_case | pluralize }} { get; set; }
            {%- endfor %}
            }
            """;

        _engine.TryParse(templateSource, out var template, out _).ShouldBeTrue();

        var ctx = _engine.CreateContext();
        ctx.SetValue("entities", FluidValue.Create(models, ctx.Options));
        ctx.SetValue("config", FluidValue.Create(config, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "dbcontext";

        var result = _engine.Render(template, ctx);

        result.ShouldContain("TestAppDbContext");
        result.ShouldContain("Products");
        result.ShouldContain("Categories");
        result.ShouldContain("Orders");
    }

    [Fact]
    public void FullPipeline_SkipExisting_SkipsExistingFiles()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        // Pre-create a file
        var existingPath = Path.Combine(outputDir, "Products.cs");
        File.WriteAllText(existingPath, "// custom code");

        var result = FileWriter.WriteFile(existingPath, "// generated", "SkipExisting");
        result.Action.ShouldBe("SkippedExisting");
        File.ReadAllText(existingPath).ShouldBe("// custom code");
    }

    [Fact]
    public void FullPipeline_IncludeExclude_FiltersModels()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig
            {
                Schema = "main",
                Include = ["Products", "Categories"]
            }
        };

        var models = IntrospectAndProcess(config);
        models.Count.ShouldBe(2);
        models.ShouldContain(m => m.Name == "Products");
        models.ShouldContain(m => m.Name == "Categories");
    }

    [Fact]
    public void FullPipeline_IncludeViews_True_IncludesViews()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Schema = "main", IncludeViews = true }
        };

        var models = IntrospectAndProcess(config, includeViews: true);
        models.ShouldContain(m => m.Name == "ActiveProducts" && m.IsView);
    }

    [Fact]
    public void FullPipeline_IncludeViews_False_ExcludesViews()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Schema = "main", IncludeViews = false }
        };

        var models = IntrospectAndProcess(config, includeViews: false);
        models.ShouldNotContain(m => m.IsView);
    }

    [Fact]
    public void FullPipeline_DispatchTag_ResolvesVariantMacros()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Namespace = "TestApp", Schema = "main" },
            Overrides =
            [
                new OverrideConfig { Class = "Products", Artifact = "entity", Variant = "Special" }
            ]
        };

        var models = IntrospectAndProcess(config);
        OverrideResolver.ApplyOverrides(models, "entity", config.Overrides, config.DataTypes);

        var products = models.First(m => m.Name == "Products");
        products.GetVariant("entity").ShouldBe("Special");

        var templateSource = """
            {%- macro DefaultClass(m) %}DEFAULT:{{ m.Name }}{%- endmacro %}
            {%- macro SpecialClass(m) %}SPECIAL:{{ m.Name }}{%- endmacro %}
            {% dispatch entity %}
            """;

        _engine.TryParse(templateSource, out var template, out _).ShouldBeTrue();
        var ctx = _engine.CreateContext();
        ctx.SetValue("entity", FluidValue.Create(products, ctx.Options));
        ctx.AmbientValues["ArtifactName"] = "entity";

        var result = _engine.Render(template, ctx).Trim();
        result.ShouldBe("SPECIAL:Products");
    }

    [Fact]
    public void FullPipeline_IgnoreOverride_RemovesProperties()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Schema = "main" },
            Overrides =
            [
                new OverrideConfig { Class = "*", Property = "Description", Artifact = "entity", Ignore = true }
            ]
        };

        var models = IntrospectAndProcess(config);
        OverrideResolver.ApplyOverrides(models, "entity", config.Overrides, config.DataTypes);

        var categories = models.First(m => m.Name == "Categories");
        categories.Attributes.ShouldNotContain(a => a.Name == "Description");
        categories.Attributes.ShouldContain(a => a.Name == "Id");
        categories.Attributes.ShouldContain(a => a.Name == "Name");
    }

    [Fact]
    public void FullPipeline_TypeMappings_ApplyCorrectly()
    {
        var config = new ProjectConfiguration
        {
            Defaults = new DefaultsConfig { Schema = "main" },
            DataTypes = new Dictionary<string, DataTypeConfig>
            {
                ["Money"] = new() { ClrType = "decimal", DefaultValue = "0m" }
            },
            TypeMappings =
            [
                new TypeMappingConfig { DbType = "REAL", DataType = "Money" }
            ]
        };

        var models = IntrospectAndProcess(config);
        var products = models.First(m => m.Name == "Products");
        var price = products.Attributes.First(a => a.Name == "Price");

        price.ClrType.ShouldBe("decimal");
        price.DefaultValue.ShouldBe("0m");
    }
}
