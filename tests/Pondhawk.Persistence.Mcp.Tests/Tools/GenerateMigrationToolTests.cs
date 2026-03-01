using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class GenerateMigrationToolTests : IDisposable
{
    private readonly string _tempDir;

    public GenerateMigrationToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_mig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void WriteDbDesign(string json)
    {
        File.WriteAllText(Path.Combine(_tempDir, "db-design.json"), json);
    }

    private static string MinimalDesignJson => """
        {
          "Origin": "design",
          "Schemas": [
            {
              "Name": "dbo",
              "Tables": [
                {
                  "Name": "Users",
                  "Columns": [
                    { "Name": "Id", "DataType": "int", "IsPrimaryKey": true, "IsIdentity": true },
                    { "Name": "Email", "DataType": "varchar(255)" }
                  ],
                  "PrimaryKey": { "Columns": ["Id"] }
                }
              ]
            }
          ]
        }
        """;

    private static string ExtendedDesignJson => """
        {
          "Origin": "design",
          "Schemas": [
            {
              "Name": "dbo",
              "Tables": [
                {
                  "Name": "Users",
                  "Columns": [
                    { "Name": "Id", "DataType": "int", "IsPrimaryKey": true, "IsIdentity": true },
                    { "Name": "Email", "DataType": "varchar(255)" },
                    { "Name": "DisplayName", "DataType": "varchar(100)" }
                  ],
                  "PrimaryKey": { "Columns": ["Id"] }
                },
                {
                  "Name": "Orders",
                  "Columns": [
                    { "Name": "Id", "DataType": "int", "IsPrimaryKey": true, "IsIdentity": true },
                    { "Name": "UserId", "DataType": "int" },
                    { "Name": "Total", "DataType": "decimal(18,2)" }
                  ],
                  "PrimaryKey": { "Columns": ["Id"] },
                  "ForeignKeys": [
                    {
                      "Name": "FK_Orders_Users",
                      "Columns": ["UserId"],
                      "PrincipalTable": "Users",
                      "PrincipalSchema": "dbo",
                      "PrincipalColumns": ["Id"]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void FirstMigration_Bootstrap()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateMigrationTool.Execute(ctx, "initial schema", provider: "sqlserver");
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Version").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("MigrationFile").GetString().ShouldNotBeNull();
        json.RootElement.GetProperty("SnapshotFile").GetString().ShouldNotBeNull();
        json.RootElement.GetProperty("Sql").GetString()!.ShouldContain("CREATE TABLE");
        json.RootElement.GetProperty("Changes").GetArrayLength().ShouldBeGreaterThan(0);

        // Verify files exist
        var sqlFile = json.RootElement.GetProperty("MigrationFile").GetString()!;
        File.Exists(Path.Combine(_tempDir, sqlFile)).ShouldBeTrue();
        var snapshotFile = json.RootElement.GetProperty("SnapshotFile").GetString()!;
        File.Exists(Path.Combine(_tempDir, snapshotFile)).ShouldBeTrue();
    }

    [Fact]
    public void SecondMigration_WithChanges()
    {
        // First: bootstrap with minimal schema
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        GenerateMigrationTool.Execute(ctx, "initial schema", provider: "sqlserver");

        // Second: update db-design.json and generate delta
        WriteDbDesign(ExtendedDesignJson);
        var result = GenerateMigrationTool.Execute(ctx, "add orders and display name", provider: "sqlserver");
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Version").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("Changes").GetArrayLength().ShouldBeGreaterThan(0);
        json.RootElement.GetProperty("Sql").GetString().ShouldNotBeNullOrEmpty();

        // Should have both V001 and V002 files
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.GetFiles(migrationsDir, "V001__*.sql").Length.ShouldBe(1);
        Directory.GetFiles(migrationsDir, "V002__*.sql").Length.ShouldBe(1);
        Directory.GetFiles(migrationsDir, "V001__*.json").Length.ShouldBe(1);
        Directory.GetFiles(migrationsDir, "V002__*.json").Length.ShouldBe(1);
    }

    [Fact]
    public void DryRun_DoesNotWriteFiles()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateMigrationTool.Execute(ctx, "initial schema", provider: "sqlserver", dryRun: true);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("DryRun").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("MigrationFile").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("SnapshotFile").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("Sql").GetString()!.ShouldContain("CREATE TABLE");

        // No migration files should exist
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.Exists(migrationsDir).ShouldBeFalse();
    }

    [Fact]
    public void NoChanges_ReturnsNoChangesWarning()
    {
        // First: bootstrap
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        GenerateMigrationTool.Execute(ctx, "initial schema", provider: "sqlserver");

        // Second: no changes
        var result = GenerateMigrationTool.Execute(ctx, "no changes", provider: "sqlserver");
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Changes").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("Version").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("Warnings").GetArrayLength().ShouldBeGreaterThan(0);

        // Should only have V001 files (no V002 since no changes)
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.GetFiles(migrationsDir, "V002__*").Length.ShouldBe(0);
    }

    [Fact]
    public void MissingDbDesign_Throws()
    {
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateMigrationTool.Execute(ctx, "test", provider: "sqlserver"));
        ex.Message.ShouldContain("db-design.json not found");
    }

    [Fact]
    public void CorruptHistory_Throws()
    {
        WriteDbDesign(MinimalDesignJson);
        // Create a .sql with no matching .json
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__orphan.sql"), "-- orphan");

        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateMigrationTool.Execute(ctx, "test", provider: "sqlserver"));
        ex.Message.ShouldContain("history validation failed");
    }

    [Fact]
    public void MissingProvider_Throws()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateMigrationTool.Execute(ctx, "test"));
        ex.Message.ShouldContain("Provider not specified");
    }

    [Fact]
    public void ResponseJsonShape()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateMigrationTool.Execute(ctx, "initial schema", provider: "sqlserver");
        var json = JsonDocument.Parse(result);

        // All expected top-level properties exist
        json.RootElement.TryGetProperty("MigrationFile", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("SnapshotFile", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("Version", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("Changes", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("Warnings", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("Sql", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("DryRun", out _).ShouldBeTrue();

        // Changes array elements have expected shape
        var firstChange = json.RootElement.GetProperty("Changes")[0];
        firstChange.TryGetProperty("Type", out _).ShouldBeTrue();
        firstChange.TryGetProperty("Description", out _).ShouldBeTrue();
    }

    [Fact]
    public void CustomOutputDirectory()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateMigrationTool.Execute(ctx, "initial schema", provider: "sqlserver", output: "custom/migrations");
        var json = JsonDocument.Parse(result);

        var sqlFile = json.RootElement.GetProperty("MigrationFile").GetString()!;
        sqlFile.ShouldContain("custom");
        File.Exists(Path.Combine(_tempDir, sqlFile)).ShouldBeTrue();
    }

    [Fact]
    public void ProviderFromConfig()
    {
        WriteDbDesign(MinimalDesignJson);
        // Create config with provider
        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "test" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        // Do NOT pass provider — it should resolve from config
        var result = GenerateMigrationTool.Execute(ctx, "initial schema");
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Version").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("Sql").GetString()!.ShouldContain("CREATE TABLE");
    }

    [Fact]
    public void ValidationError_Throws()
    {
        // Write invalid db-design.json (missing required Origin)
        File.WriteAllText(Path.Combine(_tempDir, "db-design.json"), """{ "Schemas": [] }""");
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateMigrationTool.Execute(ctx, "test", provider: "sqlserver"));
        ex.Message.ShouldContain("validation failed");
    }
}
