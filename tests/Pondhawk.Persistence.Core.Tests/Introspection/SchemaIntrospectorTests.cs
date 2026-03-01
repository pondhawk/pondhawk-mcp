using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Tests.Fixtures;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Introspection;

public class SchemaIntrospectorTests
{
    private static DefaultsConfig Defaults() => new() { Schema = "main" };

    [Fact]
    public void Introspect_BasicTable_ReturnsModelWithAttributes()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());

        models.Count.ShouldBe(1);
        var product = models[0];
        product.Name.ShouldBe("Products");
        product.IsView.ShouldBeFalse();
        product.Attributes.Count.ShouldBe(3);
        product.Attributes[0].Name.ShouldBe("Id");
        product.Attributes[0].IsPrimaryKey.ShouldBeTrue();
    }

    [Fact]
    public void Introspect_ForeignKeys_Populated()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Categories", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL")
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT, CategoryId INTEGER, FOREIGN KEY (CategoryId) REFERENCES Categories(Id)")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());

        var product = models.First(m => m.Name == "Products");
        product.ForeignKeys.Count.ShouldBe(1);
        product.ForeignKeys[0].PrincipalTable.ShouldBe("Categories");
        product.ForeignKeys[0].Columns.ShouldContain("CategoryId");
    }

    [Fact]
    public void Introspect_Indexes_Populated()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Sku TEXT")
            .AddIndex("IX_Products_Sku", "Products", "Sku", unique: true)
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());

        var product = models[0];
        product.Indexes.ShouldContain(i => i.Name == "IX_Products_Sku");
    }

    [Fact]
    public void Introspect_Views_WhenIncludeViewsTrue()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT")
            .AddView("ActiveProducts", "SELECT * FROM Products WHERE Name IS NOT NULL")
            .Build();

        var defaults = Defaults();
        defaults.IncludeViews = true;

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", defaults);
        models.ShouldContain(m => m.Name == "ActiveProducts" && m.IsView);
    }

    [Fact]
    public void Introspect_Views_ExcludedByDefault()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT")
            .AddView("ActiveProducts", "SELECT * FROM Products")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());
        models.ShouldNotContain(m => m.Name == "ActiveProducts");
    }

    [Fact]
    public void Introspect_IncludeFilter_LimitsResults()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY")
            .AddTable("Categories", "Id INTEGER PRIMARY KEY")
            .AddTable("Orders", "Id INTEGER PRIMARY KEY")
            .Build();

        var models = SchemaIntrospector.Introspect(
            db.Connection, "sqlite", Defaults(),
            include: ["Products", "Orders"]);

        models.Count.ShouldBe(2);
        models.ShouldNotContain(m => m.Name == "Categories");
    }

    [Fact]
    public void Introspect_ExcludeFilter_RemovesTables()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY")
            .AddTable("__EFMigrationsHistory", "Id TEXT PRIMARY KEY")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());
        models.ShouldNotContain(m => m.Name == "__EFMigrationsHistory");
    }

    [Fact]
    public void Introspect_WildcardInclude_Matches()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Orders", "Id INTEGER PRIMARY KEY")
            .AddTable("OrderItems", "Id INTEGER PRIMARY KEY")
            .AddTable("Products", "Id INTEGER PRIMARY KEY")
            .Build();

        var models = SchemaIntrospector.Introspect(
            db.Connection, "sqlite", Defaults(),
            include: ["Order*"]);

        models.Count.ShouldBe(2);
        models.ShouldAllBe(m => m.Name.StartsWith("Order"));
    }

    [Fact]
    public void Introspect_EmptyDatabase_ReturnsEmptyList()
    {
        using var db = new SqliteTestDatabase().Build();
        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());
        models.ShouldBeEmpty();
    }

    [Fact]
    public void Introspect_NullableColumns_MappedCorrectly()
    {
        using var db = new SqliteTestDatabase()
            .AddTable("Products", "Id INTEGER PRIMARY KEY NOT NULL, Name TEXT, Price REAL NOT NULL")
            .Build();

        var models = SchemaIntrospector.Introspect(db.Connection, "sqlite", Defaults());
        var attrs = models[0].Attributes;

        attrs.First(a => a.Name == "Name").IsNullable.ShouldBeTrue();
        attrs.First(a => a.Name == "Price").IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void ParseDatabaseName_SqlServer_ReturnsDatabase()
    {
        var name = SchemaIntrospector.ParseDatabaseName("sqlserver", "Server=localhost;Database=Inventory;Trusted_Connection=true;");
        name.ShouldBe("Inventory");
    }

    [Fact]
    public void ParseDatabaseName_Sqlite_ReturnsDataSource()
    {
        var name = SchemaIntrospector.ParseDatabaseName("sqlite", "Data Source=test.db");
        name.ShouldBe("test.db");
    }

    [Fact]
    public void MatchesWildcard_ExactMatch()
    {
        SchemaIntrospector.MatchesWildcard("Products", "Products").ShouldBeTrue();
        SchemaIntrospector.MatchesWildcard("Products", "Orders").ShouldBeFalse();
    }

    [Fact]
    public void MatchesWildcard_EndWildcard()
    {
        SchemaIntrospector.MatchesWildcard("OrderItems", "Order*").ShouldBeTrue();
        SchemaIntrospector.MatchesWildcard("Products", "Order*").ShouldBeFalse();
    }

    [Fact]
    public void MatchesWildcard_StartWildcard()
    {
        SchemaIntrospector.MatchesWildcard("AuditLog", "*Log").ShouldBeTrue();
        SchemaIntrospector.MatchesWildcard("Products", "*Log").ShouldBeFalse();
    }

    [Fact]
    public void MatchesWildcard_Star_MatchesAll()
    {
        SchemaIntrospector.MatchesWildcard("Anything", "*").ShouldBeTrue();
    }
}
