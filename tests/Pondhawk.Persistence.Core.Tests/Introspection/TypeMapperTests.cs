using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Tests.Fixtures;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Introspection;

public class TypeMapperTests
{
    [Theory]
    [InlineData("sqlserver", "int", "int")]
    [InlineData("sqlserver", "nvarchar", "string")]
    [InlineData("sqlserver", "datetime2", "DateTime")]
    [InlineData("sqlserver", "uniqueidentifier", "Guid")]
    [InlineData("sqlserver", "varbinary", "byte[]")]
    [InlineData("sqlserver", "bit", "bool")]
    [InlineData("postgresql", "integer", "int")]
    [InlineData("postgresql", "text", "string")]
    [InlineData("postgresql", "uuid", "Guid")]
    [InlineData("postgresql", "bytea", "byte[]")]
    [InlineData("postgresql", "boolean", "bool")]
    [InlineData("mysql", "int", "int")]
    [InlineData("mysql", "varchar", "string")]
    [InlineData("mysql", "tinyint(1)", "bool")]
    [InlineData("mysql", "char(36)", "Guid")]
    [InlineData("mariadb", "int", "int")]
    [InlineData("sqlite", "INTEGER", "long")]
    [InlineData("sqlite", "TEXT", "string")]
    [InlineData("sqlite", "REAL", "double")]
    [InlineData("sqlite", "BLOB", "byte[]")]
    public void BuiltInDefaults_MapCorrectly(string provider, string dbType, string expectedClr)
    {
        var mapper = new TypeMapper(provider, [], new());
        var attr = new Attribute { DataType = dbType };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe(expectedClr);
    }

    [Fact]
    public void ProjectMapping_OverridesBuiltIn()
    {
        var mappings = new List<TypeMappingConfig>
        {
            new() { DbType = "int", ClrType = "long" }
        };
        var mapper = new TypeMapper("sqlserver", mappings, new());
        var attr = new Attribute { DataType = "int" };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe("long");
    }

    [Fact]
    public void ProjectMapping_WithDataType_AppliesFullDefinition()
    {
        var dataTypes = new Dictionary<string, DataTypeConfig>
        {
            ["Uid"] = new() { ClrType = "string", MaxLength = 28, DefaultValue = "Ulid.NewUlid()" }
        };
        var mappings = new List<TypeMappingConfig>
        {
            new() { DbType = "char(28)", DataType = "Uid" }
        };
        var mapper = new TypeMapper("sqlserver", mappings, dataTypes);
        var attr = new Attribute { DataType = "char(28)" };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe("string");
        attr.MaxLength.ShouldBe(28);
        attr.DefaultValue.ShouldBe("Ulid.NewUlid()");
    }

    [Fact]
    public void UnmappedType_FallsToString()
    {
        var mapper = new TypeMapper("sqlserver", [], new());
        var attr = new Attribute { DataType = "unknown_custom_type" };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe("string");
    }

    [Fact]
    public void CaseInsensitive_DbTypeMatching()
    {
        var mapper = new TypeMapper("sqlite", [], new());
        var attr = new Attribute { DataType = "integer" };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe("long");
    }

    [Fact]
    public void AutoPopulation_ReturnsNewMappingsOnly()
    {
        var existing = new List<TypeMappingConfig>
        {
            new() { DbType = "INTEGER", ClrType = "long" }
        };
        var mapper = new TypeMapper("sqlite", existing, new());

        var discovered = new[] { "INTEGER", "TEXT", "REAL" };
        var newMappings = mapper.GetNewMappingsForAutoPopulation(discovered);

        newMappings.Count.ShouldBe(2);
        newMappings.ShouldContain(m => m.DbType == "TEXT" && m.ClrType == "string");
        newMappings.ShouldContain(m => m.DbType == "REAL" && m.ClrType == "double");
    }

    [Theory]
    [InlineData("mysql", "decimal(42,2)", "decimal")]
    [InlineData("mysql", "varchar(255)", "string")]
    [InlineData("sqlserver", "decimal(38,10)", "decimal")]
    [InlineData("sqlserver", "nvarchar(500)", "string")]
    [InlineData("postgresql", "numeric(30,6)", "decimal")]
    [InlineData("postgresql", "character varying(100)", "string")]
    public void FamilyInference_BuiltInDefaults_ResolvesCorrectly(string provider, string dbType, string expectedClr)
    {
        var mapper = new TypeMapper(provider, [], new());
        var attr = new Attribute { DataType = dbType };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe(expectedClr);
    }

    [Fact]
    public void FamilyInference_ProjectMapping_ResolvesFromBaseType()
    {
        // Project has decimal(20,2) → decimal, so decimal(42,2) should also resolve to decimal
        var mappings = new List<TypeMappingConfig>
        {
            new() { DbType = "decimal(20,2)", ClrType = "decimal" }
        };
        var mapper = new TypeMapper("mysql", mappings, new());
        var attr = new Attribute { DataType = "decimal(42,2)" };
        mapper.ApplyMapping(attr);
        attr.ClrType.ShouldBe("decimal");
    }

    [Fact]
    public void FamilyInference_AutoPopulation_UsesBaseTypeFallback()
    {
        var mapper = new TypeMapper("mysql", [], new());
        var discovered = new[] { "decimal(42,2)", "varchar(255)" };
        var newMappings = mapper.GetNewMappingsForAutoPopulation(discovered);

        newMappings.ShouldContain(m => m.DbType == "decimal(42,2)" && m.ClrType == "decimal");
        newMappings.ShouldContain(m => m.DbType == "varchar(255)" && m.ClrType == "string");
    }

    [Fact]
    public void AutoPopulation_Idempotent()
    {
        var mapper = new TypeMapper("sqlite", [], new());
        var discovered = new[] { "INTEGER", "INTEGER", "TEXT" };
        var newMappings = mapper.GetNewMappingsForAutoPopulation(discovered);
        newMappings.Count.ShouldBe(2); // no duplicates
    }

    [Fact]
    public void AutoPopulation_SqliteBacked_DiscoverTypes()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL, Data BLOB")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", new() { Schema = "main" });
        var dbTypes = models.SelectMany(m => m.Attributes.Select(a => a.DataType)).Distinct();

        var mapper = new TypeMapper("sqlite", [], new());
        var newMappings = mapper.GetNewMappingsForAutoPopulation(dbTypes);

        newMappings.Count.ShouldBeGreaterThan(0);
    }
}
