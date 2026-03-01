using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class IntrospectSchemaToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _connString;

    public IntrospectSchemaToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_introspect_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _connString = $"Data Source={_dbPath};Pooling=False";

        // Create a SQLite database with schema
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        Execute(conn, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Email TEXT)");
        Execute(conn, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total REAL, FOREIGN KEY (CustomerId) REFERENCES Customers(Id))");
        Execute(conn, "CREATE VIEW ActiveCustomers AS SELECT * FROM Customers WHERE Name IS NOT NULL");
        Execute(conn, "CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId)");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private ServerContext CreateContext(bool includeViews = false)
    {
        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = "out",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "dummy.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            },
            Defaults = new DefaultsConfig { Schema = "main", IncludeViews = includeViews }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);
        return new ServerContext(_tempDir);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void IntrospectSchema_ReturnsTablesAndWritesSchemaJson()
    {
        var ctx = CreateContext();
        var result = IntrospectSchemaTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Provider").GetString().ShouldBe("sqlite");
        json.RootElement.GetProperty("SchemaFile").GetString().ShouldNotBeNullOrEmpty();

        // Verify schema.json was written to disk
        File.Exists(ctx.SchemaPath).ShouldBeTrue();

        var summary = json.RootElement.GetProperty("Summary");
        var schemas = summary.GetProperty("Schemas");
        schemas.GetArrayLength().ShouldBeGreaterThan(0);

        var tables = schemas[0].GetProperty("Tables");
        tables.GetArrayLength().ShouldBe(2); // Customers, Orders
    }

    [Fact]
    public void IntrospectSchema_IncludesViews_WhenEnabled()
    {
        var ctx = CreateContext(includeViews: true);
        var result = IntrospectSchemaTool.Execute(ctx, includeViews: true);
        var json = JsonDocument.Parse(result);

        var schemas = json.RootElement.GetProperty("Summary").GetProperty("Schemas");
        var views = schemas[0].GetProperty("Views");
        views.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void IntrospectSchema_ExcludesViews_ByDefault()
    {
        var ctx = CreateContext();
        var result = IntrospectSchemaTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var schemas = json.RootElement.GetProperty("Summary").GetProperty("Schemas");
        var views = schemas[0].GetProperty("Views");
        views.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void IntrospectSchema_AutoPopulatesTypeMappings()
    {
        var ctx = CreateContext();
        IntrospectSchemaTool.Execute(ctx);

        // Reload config to check type mappings were written back
        var reloadedConfig = ProjectConfigurationLoader.Load(Path.Combine(_tempDir, "persistence.project.json"));
        reloadedConfig.TypeMappings.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void IntrospectSchema_IncludeFilter_LimitsResults()
    {
        var ctx = CreateContext();
        var result = IntrospectSchemaTool.Execute(ctx, include: ["Customers"]);
        var json = JsonDocument.Parse(result);

        var tables = json.RootElement.GetProperty("Summary").GetProperty("Schemas")[0].GetProperty("Tables");
        tables.GetArrayLength().ShouldBe(1);
        tables[0].GetProperty("Name").GetString().ShouldBe("Customers");
    }

    [Fact]
    public void IntrospectSchema_RefusesToOverwriteDesignOrigin()
    {
        // Write a db-design.json with Origin "design" before introspecting
        var designJson = """
            {
              "$schema": "db-design.schema.json",
              "Origin": "design",
              "Schemas": [
                {
                  "Name": "main",
                  "Tables": [
                    {
                      "Name": "DesignedTable",
                      "Columns": [
                        { "Name": "Id", "DataType": "int" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        var ctx = CreateContext();
        File.WriteAllText(ctx.SchemaPath, designJson);

        var ex = Should.Throw<InvalidOperationException>(() =>
            IntrospectSchemaTool.Execute(ctx));
        ex.Message.ShouldContain("Origin 'design'");
        ex.Message.ShouldContain("cannot be overwritten");
    }

    [Fact]
    public void IntrospectSchema_OverwritesIntrospectedOrigin()
    {
        // Write a db-design.json with Origin "introspected" — should be overwritten
        var introspectedJson = """
            {
              "Origin": "introspected",
              "Schemas": []
            }
            """;
        var ctx = CreateContext();
        File.WriteAllText(ctx.SchemaPath, introspectedJson);

        // Should succeed without error
        var result = IntrospectSchemaTool.Execute(ctx);
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("Provider").GetString().ShouldBe("sqlite");
    }

    [Fact]
    public void IntrospectSchema_OverwritesMissingOrigin()
    {
        // Write a legacy db-design.json with no Origin field — should be overwritten
        var legacyJson = """
            {
              "Database": "legacy",
              "Provider": "sqlite",
              "Schemas": []
            }
            """;
        var ctx = CreateContext();
        File.WriteAllText(ctx.SchemaPath, legacyJson);

        // Should succeed without error
        var result = IntrospectSchemaTool.Execute(ctx);
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("Provider").GetString().ShouldBe("sqlite");
    }

    [Fact]
    public void IntrospectSchema_SchemaJsonCanBeDeserialized()
    {
        var ctx = CreateContext();
        IntrospectSchemaTool.Execute(ctx);

        // Verify schema.json can be loaded back
        var models = ctx.Cache.GetSchema(ctx.SchemaPath);
        models.ShouldNotBeNull();
        models.Count.ShouldBe(2); // Customers, Orders
    }
}
