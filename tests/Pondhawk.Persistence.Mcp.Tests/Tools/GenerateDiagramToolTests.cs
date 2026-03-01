using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class GenerateDiagramToolTests : IDisposable
{
    private readonly string _tempDir;

    public GenerateDiagramToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_diagram_{Guid.NewGuid():N}");
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
                    { "Name": "Id", "DataType": "int", "IsPrimaryKey": true },
                    { "Name": "Email", "DataType": "varchar(255)" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void GeneratesHtml()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDiagramTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Summary").GetProperty("Tables").GetInt32().ShouldBe(1);

        var outputFile = json.RootElement.GetProperty("OutputFile").GetString();
        outputFile.ShouldBe("db-design.html");
        File.Exists(Path.Combine(_tempDir, outputFile!)).ShouldBeTrue();

        var html = File.ReadAllText(Path.Combine(_tempDir, outputFile!));
        html.ShouldContain("<!DOCTYPE html>");
        html.ShouldContain("Users");
    }

    [Fact]
    public void DefaultOutputPath()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDiagramTool.Execute(ctx);
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("OutputFile").GetString().ShouldBe("db-design.html");
    }

    [Fact]
    public void CustomOutputPath()
    {
        WriteDbDesign(MinimalDesignJson);
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDiagramTool.Execute(ctx, "docs/schema.html");
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("OutputFile").GetString().ShouldBe("docs/schema.html");
        File.Exists(Path.Combine(_tempDir, "docs", "schema.html")).ShouldBeTrue();
    }

    [Fact]
    public void ErrorOnMissingDbDesign()
    {
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateDiagramTool.Execute(ctx));
        ex.Message.ShouldContain("db-design.json not found");
    }

    [Fact]
    public void WorksWithoutConfig()
    {
        WriteDbDesign(MinimalDesignJson);
        // No persistence.project.json
        var ctx = new ServerContext(_tempDir);
        var result = GenerateDiagramTool.Execute(ctx);
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("Summary").GetProperty("Tables").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void ValidatesJsonSchema()
    {
        WriteDbDesign("""{ "Schemas": [] }""");
        var ctx = new ServerContext(_tempDir);
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateDiagramTool.Execute(ctx));
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
        var result = GenerateDiagramTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("OutputFile").GetString().ShouldBe("connect-accounting.html");
        File.Exists(Path.Combine(_tempDir, "connect-accounting.html")).ShouldBeTrue();
    }

    [Fact]
    public void ProjectNameInDiagramTitle()
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
        GenerateDiagramTool.Execute(ctx);

        var html = File.ReadAllText(Path.Combine(_tempDir, "connect-accounting.html"));
        html.ShouldContain("<title>ER Diagram — connect-accounting</title>");
    }

    [Fact]
    public void DescriptionAppearsInDiagram()
    {
        WriteDbDesign(MinimalDesignJson);
        var config = new ProjectConfiguration
        {
            ProjectName = "connect-accounting",
            Description = "Financial system schema",
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "test" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        GenerateDiagramTool.Execute(ctx);

        var html = File.ReadAllText(Path.Combine(_tempDir, "connect-accounting.html"));
        html.ShouldContain("Financial system schema");
        html.ShouldContain("titlebar-desc");
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
        var result = GenerateDiagramTool.Execute(ctx);
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("Summary").GetProperty("Relationships").GetInt32().ShouldBe(1);

        var html = File.ReadAllText(Path.Combine(_tempDir, "db-design.html"));
        html.ShouldContain("Customers");
        html.ShouldContain("Orders");
    }
}
