using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class ValidateConfigToolTests : IDisposable
{
    private readonly string _tempDir;

    public ValidateConfigToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_vc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ValidateConfig_ValidConfig_ReturnsNoErrors()
    {
        // Create a valid config with existing template file
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "{{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "Data Source=localhost" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ValidateConfigTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Valid").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void ValidateConfig_MissingConfig_ReturnsError()
    {
        var ctx = new ServerContext(_tempDir);
        var result = ValidateConfigTool.Execute(ctx);

        result.ShouldContain("not found");
    }

    [Fact]
    public void ValidateConfig_MissingTemplateFile_ReportsError()
    {
        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "Data Source=localhost" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/missing.liquid", OutputPattern = "Entities/{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ValidateConfigTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void ValidateConfig_InvalidLoggingLevel_ReportsError()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "{{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "Data Source=localhost" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            },
            Logging = new LoggingConfig { Enabled = true, Level = "InvalidLevel" }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ValidateConfigTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void ValidateConfig_InvalidDataTypeReference_ReportsError()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity.liquid"), "{{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "Data Source=localhost" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            },
            TypeMappings = [new() { DbType = "money", DataType = "NonExistent" }]
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ValidateConfigTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.ShouldContain("NonExistent");
    }

    [Fact]
    public void ValidateConfig_OutputPathCollision_ReportsWarning()
    {
        var templatesDir = Path.Combine(_tempDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "entity1.liquid"), "{{ entity.Name }}");
        File.WriteAllText(Path.Combine(templatesDir, "entity2.liquid"), "{{ entity.Name }}");

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlserver", ConnectionString = "Data Source=localhost" },
            OutputDir = "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity1"] = new() { Path = "templates/entity1.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" },
                ["entity2"] = new() { Path = "templates/entity2.liquid", OutputPattern = "{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ValidateConfigTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var warnings = json.RootElement.GetProperty("Warnings");
        warnings.GetArrayLength().ShouldBeGreaterThan(0);
        result.ShouldContain("collision");
    }
}
