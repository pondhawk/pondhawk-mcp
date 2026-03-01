using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class GenerateDdlToolTests : IDisposable
{
    private readonly string _tempDir;

    public GenerateDdlToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_ddl_{Guid.NewGuid():N}");
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
              "Name": "public",
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

    [Fact]
    public void GeneratesDdl_SqlServer()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "sqlserver");
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Provider").GetString().ShouldBe("sqlserver");
        json.RootElement.GetProperty("Summary").GetProperty("Tables").GetInt32().ShouldBe(1);

        var outputFile = json.RootElement.GetProperty("OutputFile").GetString();
        outputFile.ShouldBe("db-design.sqlserver.sql");
        File.Exists(Path.Combine(_tempDir, outputFile!)).ShouldBeTrue();

        var ddl = File.ReadAllText(Path.Combine(_tempDir, outputFile!));
        ddl.ShouldContain("CREATE TABLE");
        ddl.ShouldContain("Users");
    }

    [Fact]
    public void GeneratesDdl_PostgreSql()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "postgresql");

        var ddl = File.ReadAllText(Path.Combine(_tempDir, "db-design.postgresql.sql"));
        ddl.ShouldContain("GENERATED ALWAYS AS IDENTITY");
    }

    [Fact]
    public void GeneratesDdl_Sqlite()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        GenerateDdlTool.Execute(ctx, "sqlite");

        var ddl = File.ReadAllText(Path.Combine(_tempDir, "db-design.sqlite.sql"));
        ddl.ShouldContain("AUTOINCREMENT");
    }

    [Fact]
    public void GeneratesDdl_MySQL()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        GenerateDdlTool.Execute(ctx, "mysql");

        var ddl = File.ReadAllText(Path.Combine(_tempDir, "db-design.mysql.sql"));
        ddl.ShouldContain("ENGINE = INNODB");
    }

    [Fact]
    public void DefaultOutputPath()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "sqlserver");
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("OutputFile").GetString().ShouldBe("db-design.sqlserver.sql");
    }

    [Fact]
    public void CustomOutputPath()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "sqlserver", "custom/output.sql");
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("OutputFile").GetString().ShouldBe("custom/output.sql");
        File.Exists(Path.Combine(_tempDir, "custom", "output.sql")).ShouldBeTrue();
    }

    [Fact]
    public void ErrorOnMissingDbDesign()
    {
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateDdlTool.Execute(ctx, "sqlserver"));
        ex.Message.ShouldContain("db-design.json not found");
    }

    [Fact]
    public void ErrorOnInvalidProvider()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<ArgumentException>(() =>
            GenerateDdlTool.Execute(ctx, "oracle"));
        ex.Message.ShouldContain("Invalid provider");
    }

    [Fact]
    public void WorksWithoutConfig()
    {
        WriteDbDesign(MinimalDesignJson);
        // No persistence.project.json — should still generate DDL
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "sqlserver");
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("Summary").GetProperty("Tables").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void ValidatesJsonSchema()
    {
        // Missing required Origin field
        WriteDbDesign("""{ "Schemas": [] }""");
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateDdlTool.Execute(ctx, "sqlserver"));
        ex.Message.ShouldContain("validation failed");
    }

    [Fact]
    public void ProjectNameUsedInDefaultOutputPath()
    {
        WriteDbDesign(MinimalDesignJson);
        var config = new ProjectConfiguration
        {
            ProjectName = "connect-accounting",
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "test" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "sqlserver");
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("OutputFile").GetString().ShouldBe("connect-accounting.sqlserver.sql");
        File.Exists(Path.Combine(_tempDir, "connect-accounting.sqlserver.sql")).ShouldBeTrue();
    }

    [Fact]
    public void ProjectNameAndDescriptionInDdlHeader()
    {
        WriteDbDesign(MinimalDesignJson);
        var config = new ProjectConfiguration
        {
            ProjectName = "connect-accounting",
            Description = "Accounting database",
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "test" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        GenerateDdlTool.Execute(ctx, "sqlserver");

        var ddl = File.ReadAllText(Path.Combine(_tempDir, "connect-accounting.sqlserver.sql"));
        ddl.ShouldContain("Generated by pondhawk-mcp for connect-accounting");
        ddl.ShouldContain("-- Accounting database");
    }

    [Fact]
    public void MergesRelationshipsFromConfig()
    {
        var designJson = """
            {
              "Origin": "design",
              "Schemas": [
                {
                  "Name": "dbo",
                  "Tables": [
                    {
                      "Name": "Orders",
                      "Columns": [
                        { "Name": "Id", "DataType": "int" },
                        { "Name": "CustomerId", "DataType": "int" }
                      ]
                    },
                    {
                      "Name": "Customers",
                      "Columns": [
                        { "Name": "Id", "DataType": "int" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        WriteDbDesign(designJson);

        // Create config with explicit relationship
        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "test" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            },
            Defaults = new DefaultsConfig { Schema = "dbo" },
            Relationships =
            [
                new RelationshipConfig
                {
                    DependentTable = "Orders",
                    DependentColumns = ["CustomerId"],
                    PrincipalTable = "Customers",
                    PrincipalColumns = ["Id"]
                }
            ]
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = GenerateDdlTool.Execute(ctx, "sqlserver");
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("Summary").GetProperty("ForeignKeys").GetInt32().ShouldBe(1);

        var ddl = File.ReadAllText(Path.Combine(_tempDir, "db-design.sqlserver.sql"));
        ddl.ShouldContain("FOREIGN KEY");
        ddl.ShouldContain("Customers");
    }
}
