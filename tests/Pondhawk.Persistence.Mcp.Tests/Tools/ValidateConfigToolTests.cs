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
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_validate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        File.WriteAllText(Path.Combine(_tempDir, "templates", "entity.liquid"), "class {{ item.Name }} {}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static ProjectConfiguration ValidConfig() => new()
    {
        OutputDir = "src/Generated",
        Templates = new Dictionary<string, TemplateConfig>
        {
            ["entity"] = new()
            {
                Path = "templates/entity.liquid",
                OutputPattern = "{{ item.Name }}.cs",
                Scope = "PerItem",
                Mode = "Always"
            }
        }
    };

    private string Validate(ProjectConfiguration config)
    {
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);
        return ValidateConfigTool.Execute(new ServerContext(_tempDir));
    }

    private void WriteModel(string json) => File.WriteAllText(Path.Combine(_tempDir, "model.json"), json);

    [Fact]
    public void ValidateConfig_ValidConfig_ReturnsNoErrors()
    {
        var result = Validate(ValidConfig());

        JsonDocument.Parse(result).RootElement.GetProperty("Valid").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void ValidateConfig_MissingConfig_ReturnsError()
    {
        var result = ValidateConfigTool.Execute(new ServerContext(_tempDir));

        result.ShouldContain("not found");
    }

    [Fact]
    public void ValidateConfig_MissingTemplateFile_ReportsError()
    {
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/nope.liquid";

        var result = Validate(config);

        JsonDocument.Parse(result).RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.ShouldContain("File not found");
    }

    [Fact]
    public void ValidateConfig_InvalidLoggingLevel_ReportsError()
    {
        var config = ValidConfig();
        config.Logging.Level = "Chatty";

        var result = Validate(config);

        JsonDocument.Parse(result).RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.ShouldContain("Invalid level");
    }

    [Fact]
    public void ValidateConfig_OverrideNamingUnknownArtifact_ReportsError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Product/Price", Artifact = "nosuch", Variant = "Currency" });

        var result = Validate(config);

        JsonDocument.Parse(result).RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.ShouldContain("not a configured template");
    }

    [Fact]
    public void ValidateConfig_OverrideMatchingNoNode_ReportsWarning()
    {
        WriteModel("""{ "Nodes": [ { "Name": "Product", "Kind": "Class" } ] }""");
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Typo/Price", Artifact = "entity", Variant = "Currency" });

        var result = Validate(config);
        var root = JsonDocument.Parse(result).RootElement;

        root.GetProperty("Valid").GetBoolean().ShouldBeTrue();
        root.GetProperty("Warnings").GetArrayLength().ShouldBeGreaterThan(0);
        result.ShouldContain("matches no node");
    }

    [Fact]
    public void ValidateConfig_MalformedModel_ReportsError()
    {
        WriteModel("{ not json");

        var result = Validate(ValidConfig());

        JsonDocument.Parse(result).RootElement.GetProperty("Valid").GetBoolean().ShouldBeFalse();
        result.ShouldContain("model.json");
    }

    [Fact]
    public void ValidateConfig_OutputPathCollision_ReportsWarning()
    {
        File.WriteAllText(Path.Combine(_tempDir, "templates", "other.liquid"), "{{ item.Name }}");
        var config = ValidConfig();
        config.Templates["duplicate"] = new TemplateConfig
        {
            Path = "templates/other.liquid",
            OutputPattern = "{{ item.Name }}.cs",
            Scope = "PerItem",
            Mode = "Always"
        };

        var result = Validate(config);

        JsonDocument.Parse(result).RootElement.GetProperty("Warnings").GetArrayLength().ShouldBeGreaterThan(0);
        result.ShouldContain("collision");
    }
}
