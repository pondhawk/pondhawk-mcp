using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class GenerateToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _connString;

    public GenerateToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_generate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _connString = $"Data Source={_dbPath};Pooling=False";

        // Create a SQLite database with schema
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        Execute(conn, "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL)");
        Execute(conn, "CREATE TABLE Categories (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL)");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private ServerContext CreateContext(string? templateContent = null, string scope = "PerModel", string mode = "Always")
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);

        var template = templateContent ?? "// Generated: {{ entity.Name }}";
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), template);

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{entity.Name}}.cs",
                    Scope = scope,
                    Mode = mode
                }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);
        return new ServerContext(_tempDir);
    }

    private void IntrospectFirst(ServerContext ctx)
    {
        // Run introspect to create schema.json
        IntrospectSchemaTool.Execute(ctx);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Generate_ProducesOutputFiles()
    {
        var ctx = CreateContext();
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        files.GetArrayLength().ShouldBe(2); // Products, Categories
    }

    [Fact]
    public void Generate_ModelsParam_FiltersOutput()
    {
        var ctx = CreateContext();
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx, models: ["Products"]);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        files.GetArrayLength().ShouldBe(1);
        files[0].GetProperty("Path").GetString().ShouldBe("Products.cs");
    }

    [Fact]
    public void Generate_TemplatesParam_FiltersTemplates()
    {
        // Add a second template
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "// {{ entity.Name }}");
        File.WriteAllText(Path.Combine(templatesDir, "stub.liquid"), "// stub {{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" },
                ["stub"] = new() { Path = "templates/stub.liquid", OutputPattern = "{{entity.Name}}.stub.cs", Scope = "PerModel", Mode = "Always" }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx, templates: ["stub"]);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        // Only stub templates generated (2 tables * 1 template = 2 files)
        files.GetArrayLength().ShouldBe(2);
        files[0].GetProperty("Path").GetString()!.ShouldContain(".stub.cs");
    }

    [Fact]
    public void Generate_SkipExisting_DoesNotOverwrite()
    {
        var ctx = CreateContext(mode: "SkipExisting");
        IntrospectFirst(ctx);

        // First generation creates files
        GenerateTool.Execute(ctx);

        // Second generation should skip
        ctx = CreateContext(mode: "SkipExisting");
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var summary = json.RootElement.GetProperty("Summary").GetString()!;
        summary.ShouldContain("skipped");
    }

    [Fact]
    public void Generate_ThrowsWhenNoSchemaJson()
    {
        var ctx = CreateContext();
        // Do NOT run introspect — schema.json should not exist
        var ex = Should.Throw<InvalidOperationException>(() =>
            GenerateTool.Execute(ctx));
        ex.Message.ShouldContain("db-design.json not found");
    }

    [Fact]
    public void Generate_ParametersPassedToTemplate()
    {
        var ctx = CreateContext(templateContent: "// Version: {{ parameters.version }}");
        IntrospectFirst(ctx);
        var parameters = new Dictionary<string, object> { ["version"] = "1.0" };
        var result = GenerateTool.Execute(ctx, parameters: parameters, models: ["Products"]);

        var outputFile = Path.Combine(_tempDir, "output", "Products.cs");
        File.Exists(outputFile).ShouldBeTrue();
        File.ReadAllText(outputFile).ShouldContain("Version: 1.0");
    }

    [Fact]
    public void Generate_AppliesToTables_SkipsViews()
    {
        // Add a view to the database
        using (var conn = new SqliteConnection(_connString))
        {
            conn.Open();
            Execute(conn, "CREATE VIEW ProductSummary AS SELECT Id, Name FROM Products");
        }

        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "// {{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{entity.Name}}.cs",
                    Scope = "PerModel",
                    Mode = "Always",
                    AppliesTo = "Tables"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main", IncludeViews = true }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        files.GetArrayLength().ShouldBe(2); // Products, Categories — NOT ProductSummary
        var paths = Enumerable.Range(0, files.GetArrayLength())
            .Select(i => files[i].GetProperty("Path").GetString()!).ToList();
        paths.ShouldNotContain("ProductSummary.cs");
    }

    [Fact]
    public void Generate_AppliesToViews_SkipsTables()
    {
        using (var conn = new SqliteConnection(_connString))
        {
            conn.Open();
            Execute(conn, "CREATE VIEW ProductSummary AS SELECT Id, Name FROM Products");
        }

        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "// {{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{entity.Name}}.cs",
                    Scope = "PerModel",
                    Mode = "Always",
                    AppliesTo = "Views"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main", IncludeViews = true }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        files.GetArrayLength().ShouldBe(1); // Only ProductSummary
        files[0].GetProperty("Path").GetString().ShouldBe("ProductSummary.cs");
    }

    [Fact]
    public void Generate_AppliesToOmitted_RunsForAll()
    {
        using (var conn = new SqliteConnection(_connString))
        {
            conn.Open();
            Execute(conn, "CREATE VIEW ProductSummary AS SELECT Id, Name FROM Products");
        }

        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "// {{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{entity.Name}}.cs",
                    Scope = "PerModel",
                    Mode = "Always"
                    // AppliesTo omitted — should run for all
                }
            },
            Defaults = new DefaultsConfig { Schema = "main", IncludeViews = true }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        files.GetArrayLength().ShouldBe(3); // Products, Categories, ProductSummary
    }

    [Fact]
    public void Generate_WhitespaceOnlyOutput_SkippedEmpty()
    {
        var ctx = CreateContext(templateContent: "   \n  \t  ");
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        foreach (var file in files.EnumerateArray())
        {
            file.GetProperty("Action").GetString().ShouldBe("SkippedEmpty");
        }

        var summary = json.RootElement.GetProperty("Summary").GetString()!;
        summary.ShouldContain("skipped");

        // Verify no files were written to disk
        var outputDir = json.RootElement.GetProperty("OutputDir").GetString()!;
        if (Directory.Exists(outputDir))
            Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public void Generate_PerEntityError_ReportsFailedAction()
    {
        // Template referencing undefined variable will fail at render time
        var ctx = CreateContext(templateContent: "{{ undefined_var.missing }}");
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var summary = json.RootElement.GetProperty("Summary").GetString()!;
        summary.ShouldContain("failed");
    }

    [Fact]
    public void Generate_SingleFileScope_ProducesOneFile()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "all.liquid"),
            "// Tables: {% for e in entities %}{{ e.Name }} {% endfor %}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["all"] = new()
                {
                    Path = "templates/all.liquid",
                    OutputPattern = "AllEntities.cs",
                    Scope = "SingleFile",
                    Mode = "Always"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var files = json.RootElement.GetProperty("FilesWritten");
        files.GetArrayLength().ShouldBe(1);
        files[0].GetProperty("Path").GetString().ShouldBe("AllEntities.cs");

        var content = File.ReadAllText(Path.Combine(_tempDir, "output", "AllEntities.cs"));
        content.ShouldContain("Products");
        content.ShouldContain("Categories");
    }

    [Fact]
    public void Generate_SingleFileScope_WithParameters()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "all.liquid"),
            "// Version: {{ parameters.version }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["all"] = new()
                {
                    Path = "templates/all.liquid",
                    OutputPattern = "index.cs",
                    Scope = "SingleFile",
                    Mode = "Always"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var parameters = new Dictionary<string, object> { ["version"] = "2.0" };
        var result = GenerateTool.Execute(ctx, parameters: parameters);
        var json = JsonDocument.Parse(result);

        var content = File.ReadAllText(Path.Combine(_tempDir, "output", "index.cs"));
        content.ShouldContain("Version: 2.0");
    }

    [Fact]
    public void Generate_SingleFileScope_SkipExisting()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "all.liquid"), "// All entities");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["all"] = new()
                {
                    Path = "templates/all.liquid",
                    OutputPattern = "index.cs",
                    Scope = "SingleFile",
                    Mode = "SkipExisting"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);

        // First generation creates the file
        GenerateTool.Execute(ctx);

        // Second should skip
        ctx = new ServerContext(_tempDir);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var summary = json.RootElement.GetProperty("Summary").GetString()!;
        summary.ShouldContain("skipped");
    }

    [Fact]
    public void Generate_SingleFileScope_Error()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "all.liquid"),
            "{{ undefined_var.missing }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["all"] = new()
                {
                    Path = "templates/all.liquid",
                    OutputPattern = "index.cs",
                    Scope = "SingleFile",
                    Mode = "Always"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var summary = json.RootElement.GetProperty("Summary").GetString()!;
        summary.ShouldContain("failed");
    }

    [Fact]
    public void Generate_SingleFileScope_Overwrite()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "all.liquid"), "// All entities v1");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = _connString },
            OutputDir = Path.Combine(_tempDir, "output"),
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["all"] = new()
                {
                    Path = "templates/all.liquid",
                    OutputPattern = "index.cs",
                    Scope = "SingleFile",
                    Mode = "Always"
                }
            },
            Defaults = new DefaultsConfig { Schema = "main" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        IntrospectFirst(ctx);

        // First generation creates the file
        GenerateTool.Execute(ctx);
        File.Exists(Path.Combine(_tempDir, "output", "index.cs")).ShouldBeTrue();

        // Update template and regenerate — should overwrite
        File.WriteAllText(Path.Combine(templatesDir, "all.liquid"), "// All entities v2");
        ctx = new ServerContext(_tempDir);
        var result = GenerateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var summary = json.RootElement.GetProperty("Summary").GetString()!;
        summary.ShouldContain("written");

        var content = File.ReadAllText(Path.Combine(_tempDir, "output", "index.cs"));
        content.ShouldContain("v2");
    }
}
