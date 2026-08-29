using Pondhawk.Persistence.Core.Configuration;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Configuration;

public class ConfigurationValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigurationValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_val_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        WriteTemplate("entity.liquid", "class {{ item.Name }} {}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void WriteTemplate(string name, string content)
        => File.WriteAllText(Path.Combine(_tempDir, "templates", name), content);

    private void WriteModel(string json) => File.WriteAllText(Path.Combine(_tempDir, "model.json"), json);

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

    private ValidationResult Validate(ProjectConfiguration config)
        => ConfigurationValidator.Validate(config, _tempDir);

    // --- required sections ---

    [Fact]
    public void ValidConfig_HasNoErrors()
    {
        var result = Validate(ValidConfig());
        result.Errors.ShouldBeEmpty();
        result.Valid.ShouldBeTrue();
    }

    [Fact]
    public void MissingOutputDir_IsAnError()
    {
        var config = ValidConfig();
        config.OutputDir = "";

        Validate(config).Errors.ShouldContain(e => e.Contains("OutputDir"));
    }

    [Fact]
    public void MissingTemplates_IsAnError()
    {
        var config = ValidConfig();
        config.Templates.Clear();

        Validate(config).Errors.ShouldContain(e => e.Contains("Templates"));
    }

    // --- templates ---

    [Fact]
    public void TemplateFileNotFound_IsAnError()
    {
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/missing.liquid";

        Validate(config).Errors.ShouldContain(e => e.Contains("File not found"));
    }

    [Fact]
    public void TemplateWithSyntaxError_IsAnError()
    {
        WriteTemplate("broken.liquid", "{% for x in %}");
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/broken.liquid";

        Validate(config).Errors.ShouldContain(e => e.Contains("parse error"));
    }

    [Fact]
    public void UnknownFilter_IsAWarning()
    {
        WriteTemplate("filter.liquid", "{{ item.Name | no_such_filter }}");
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/filter.liquid";

        var result = Validate(config);
        result.Warnings.ShouldContain(w => w.Contains("no_such_filter"));
        result.Valid.ShouldBeTrue();
    }

    [Fact]
    public void InvalidScope_IsAnError()
    {
        var config = ValidConfig();
        config.Templates["entity"].Scope = "PerModel";

        Validate(config).Errors.ShouldContain(e => e.Contains("Invalid scope"));
    }

    [Fact]
    public void InvalidMode_IsAnError()
    {
        var config = ValidConfig();
        config.Templates["entity"].Mode = "Sometimes";

        Validate(config).Errors.ShouldContain(e => e.Contains("Invalid mode"));
    }

    [Fact]
    public void MissingOutputPattern_IsAnError()
    {
        var config = ValidConfig();
        config.Templates["entity"].OutputPattern = "";

        Validate(config).Errors.ShouldContain(e => e.Contains("OutputPattern"));
    }

    [Fact]
    public void AppliesToMatchingNoKindInModel_IsAWarning()
    {
        WriteModel("""{ "Nodes": [ { "Name": "Product", "Kind": "Class" } ] }""");
        var config = ValidConfig();
        config.Templates["entity"].AppliesTo = "Endpoint";

        var result = Validate(config);
        result.Warnings.ShouldContain(w => w.Contains("Endpoint") && w.Contains("generate nothing"));
        result.Valid.ShouldBeTrue();
    }

    [Fact]
    public void AppliesToMatchingAKind_IsClean()
    {
        WriteModel("""{ "Nodes": [ { "Name": "Product", "Kind": "Class" } ] }""");
        var config = ValidConfig();
        config.Templates["entity"].AppliesTo = "Class";

        Validate(config).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void AppliesToIsNotCheckedWithoutAModel()
    {
        var config = ValidConfig();
        config.Templates["entity"].AppliesTo = "Anything";

        Validate(config).Warnings.ShouldBeEmpty();
    }

    // --- overrides ---

    [Fact]
    public void OverrideWithoutPath_IsAnError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Variant = "Currency", Artifact = "entity" });

        Validate(config).Errors.ShouldContain(e => e.Contains("'Path' is required"));
    }

    [Fact]
    public void OverrideWithVariantButNoArtifact_IsAnError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Product/Price", Variant = "Currency" });

        Validate(config).Errors.ShouldContain(e => e.Contains("'Artifact' is required"));
    }

    [Fact]
    public void OverrideDoingNothing_IsAnError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Product/Price", Artifact = "entity" });

        Validate(config).Errors.ShouldContain(e => e.Contains("at least one of"));
    }

    [Fact]
    public void OverrideNamingAnUnknownArtifact_IsAnError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Product/Price", Artifact = "dto", Variant = "Currency" });

        Validate(config).Errors.ShouldContain(e => e.Contains("not a configured template"));
    }

    [Fact]
    public void OverrideMatchingNoNode_IsAWarning()
    {
        WriteModel("""
            { "Nodes": [ { "Name": "Product", "Kind": "Class",
              "Children": [ { "Name": "Id", "Kind": "Property" } ] } ] }
            """);
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Product/Typo", Artifact = "entity", Variant = "Currency" });

        var result = Validate(config);
        result.Warnings.ShouldContain(w => w.Contains("matches no node"));
        result.Valid.ShouldBeTrue();
    }

    [Fact]
    public void OverrideMatchingANode_IsClean()
    {
        WriteModel("""
            { "Nodes": [ { "Name": "Product", "Kind": "Class",
              "Children": [ { "Name": "Price", "Kind": "Property" } ] } ] }
            """);
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Path = "Product/Price", Artifact = "entity", Variant = "Currency" });

        Validate(config).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void OverrideWithMetadataOnly_IsValid()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig
        {
            Path = "Product/Price",
            Metadata = new Dictionary<string, object?> { ["Type"] = "decimal" }
        });

        Validate(config).Errors.ShouldBeEmpty();
    }

    // --- model ---

    [Fact]
    public void MalformedModel_IsAnError()
    {
        WriteModel("{ not json");

        Validate(ValidConfig()).Errors.ShouldContain(e => e.Contains("model.json"));
    }

    [Fact]
    public void ModelWithNodeMissingKind_IsAnError()
    {
        WriteModel("""{ "Nodes": [ { "Name": "Product" } ] }""");

        Validate(ValidConfig()).Errors.ShouldContain(e => e.Contains("model.json"));
    }

    // --- logging ---

    [Fact]
    public void InvalidLogLevel_IsAnError()
    {
        var config = ValidConfig();
        config.Logging.Level = "Chatty";

        Validate(config).Errors.ShouldContain(e => e.Contains("Invalid level"));
    }

    [Fact]
    public void InvalidRollingInterval_IsAnError()
    {
        var config = ValidConfig();
        config.Logging.RollingInterval = "Fortnight";

        Validate(config).Errors.ShouldContain(e => e.Contains("rolling interval"));
    }

    // --- values / env ---

    [Fact]
    public void UnresolvedEnvVarInValues_IsAWarning()
    {
        var config = ValidConfig();
        config.Values["LicenceKey"] = "${PONDHAWK_TEST_UNSET_VAR}";

        var result = Validate(config);
        result.Warnings.ShouldContain(w => w.Contains("PONDHAWK_TEST_UNSET_VAR"));
        result.Valid.ShouldBeTrue();
    }

    [Fact]
    public void NonStringValues_AreNotScannedForEnvVars()
    {
        var config = ValidConfig();
        config.Values["Retries"] = 3L;

        Validate(config).Warnings.ShouldBeEmpty();
    }

    // --- collisions ---

    [Fact]
    public void TwoTemplatesWithTheSameOutput_IsAWarning()
    {
        WriteTemplate("other.liquid", "class {{ item.Name }} {}");
        var config = ValidConfig();
        config.Templates["duplicate"] = new TemplateConfig
        {
            Path = "templates/other.liquid",
            OutputPattern = "{{ item.Name }}.cs",
            Scope = "PerItem",
            Mode = "Always"
        };

        Validate(config).Warnings.ShouldContain(w => w.Contains("collision"));
    }

    // --- schema-aware overload ---

    [Fact]
    public void RawJsonOverload_ReportsSchemaViolations()
    {
        var json = """
            {
              "OutputDir": "src/Generated",
              "Templates": {
                "entity": {
                  "Path": "templates/entity.liquid",
                  "OutputPattern": "{{ item.Name }}.cs",
                  "Scope": "NotAScope",
                  "Mode": "Always"
                }
              }
            }
            """;

        var config = ProjectConfigurationLoader.Deserialize(json);
        ConfigurationValidator.Validate(json, config, _tempDir).Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void RawJsonOverload_AcceptsAValidConfig()
    {
        var json = """
            {
              "OutputDir": "src/Generated",
              "Templates": {
                "entity": {
                  "Path": "templates/entity.liquid",
                  "OutputPattern": "{{ item.Name }}.cs",
                  "Scope": "PerItem",
                  "Mode": "Always"
                }
              },
              "Values": { "Namespace": "MyApp" }
            }
            """;

        var config = ProjectConfigurationLoader.Deserialize(json);
        ConfigurationValidator.Validate(json, config, _tempDir).Errors.ShouldBeEmpty();
    }
}
