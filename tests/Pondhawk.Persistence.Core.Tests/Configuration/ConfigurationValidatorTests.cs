using Pondhawk.Persistence.Core.Configuration;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Configuration;

public class ConfigurationValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigurationValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void CreateTemplateFile(string relativePath, string content = "{{ entity.Name }}")
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private ProjectConfiguration ValidConfig()
    {
        CreateTemplateFile("templates/entity.liquid");
        return new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = "Data Source=test.db" },
            OutputDir = "src/Data",
            Templates = new()
            {
                ["entity"] = new TemplateConfig
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "Entities/{{entity.Name}}.cs",
                    Scope = "PerModel",
                    Mode = "Always"
                }
            }
        };
    }

    [Fact]
    public void Validate_ValidConfig_NoErrors()
    {
        var config = ValidConfig();
        var result = ConfigurationValidator.Validate(config, _tempDir);

        result.Valid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_MissingConnection_ReportsError()
    {
        var config = ValidConfig();
        config.Connection = new ConnectionConfig();

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Connection"));
    }

    [Fact]
    public void Validate_MissingOutputDir_ReportsError()
    {
        var config = ValidConfig();
        config.OutputDir = "";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("OutputDir"));
    }

    [Fact]
    public void Validate_MissingTemplates_ReportsError()
    {
        var config = ValidConfig();
        config.Templates.Clear();

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Templates"));
    }

    [Fact]
    public void Validate_InvalidProvider_ReportsError()
    {
        var config = ValidConfig();
        config.Connection.Provider = "oracle";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("oracle"));
    }

    [Fact]
    public void Validate_InvalidScope_ReportsError()
    {
        var config = ValidConfig();
        config.Templates["entity"].Scope = "Invalid";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Invalid", StringComparison.OrdinalIgnoreCase) && e.Contains("scope", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_InvalidMode_ReportsError()
    {
        var config = ValidConfig();
        config.Templates["entity"].Mode = "BadMode";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("BadMode") && e.Contains("Mode"));
    }

    [Fact]
    public void Validate_MissingTemplateFile_ReportsError()
    {
        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = "Data Source=test.db" },
            OutputDir = "src/Data",
            Templates = new()
            {
                ["entity"] = new TemplateConfig
                {
                    Path = "templates/missing.liquid",
                    OutputPattern = "out.cs",
                    Scope = "PerModel",
                    Mode = "Always"
                }
            }
        };

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("File not found"));
    }

    [Fact]
    public void Validate_InvalidLiquidTemplate_ReportsError()
    {
        CreateTemplateFile("templates/bad.liquid", "{% if %}");
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/bad.liquid";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("parse error"));
    }

    [Fact]
    public void Validate_InvalidDataTypeReference_ReportsError()
    {
        var config = ValidConfig();
        config.TypeMappings.Add(new TypeMappingConfig { DbType = "int", DataType = "NonExistent" });

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("NonExistent") && e.Contains("DataTypes"));
    }

    [Fact]
    public void Validate_InvalidLogLevel_ReportsError()
    {
        var config = ValidConfig();
        config.Logging.Level = "Trace";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Trace") && e.Contains("level"));
    }

    [Fact]
    public void Validate_InvalidRollingInterval_ReportsError()
    {
        var config = ValidConfig();
        config.Logging.RollingInterval = "Second";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Second") && e.Contains("rolling interval"));
    }

    [Fact]
    public void Validate_OverrideVariantWithoutArtifact_ReportsError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Class = "Foo", Variant = "Bar" });

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Artifact") && e.Contains("required"));
    }

    [Fact]
    public void Validate_OverrideWithNoAction_ReportsError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Class = "Foo" });

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Must specify at least one"));
    }

    [Fact]
    public void Validate_OverrideInvalidDataType_ReportsError()
    {
        var config = ValidConfig();
        config.Overrides.Add(new OverrideConfig { Class = "Foo", DataType = "Missing" });

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("Missing") && e.Contains("DataTypes"));
    }

    [Fact]
    public void Validate_EmptyRelationshipFields_ReportsErrors()
    {
        var config = ValidConfig();
        config.Relationships.Add(new RelationshipConfig());

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("DependentTable"));
        result.Errors.ShouldContain(e => e.Contains("PrincipalTable"));
    }

    [Fact]
    public void Validate_UnresolvedEnvVars_ReportsWarning()
    {
        var config = ValidConfig();
        config.Connection.ConnectionString = "${UNSET_VAR}";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Warnings.ShouldContain(w => w.Contains("UNSET_VAR"));
    }

    [Fact]
    public void Validate_OutputPathCollision_ReportsWarning()
    {
        CreateTemplateFile("templates/a.liquid");
        CreateTemplateFile("templates/b.liquid");
        var config = ValidConfig();
        config.Templates["dup"] = new TemplateConfig
        {
            Path = "templates/b.liquid",
            OutputPattern = "Entities/{{entity.Name}}.cs",
            Scope = "PerModel",
            Mode = "Always"
        };

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Warnings.ShouldContain(w => w.Contains("collision"));
    }

    [Fact]
    public void Validate_TemplateWithMacroAndDispatch_Passes()
    {
        var templateContent = """
            {% macro DefaultClass(entity) %}
            public class {{ entity.Name }} { }
            {% endmacro %}
            {% for attr in entity.Attributes %}
            {% dispatch attr %}
            {% endfor %}
            """;
        CreateTemplateFile("templates/dispatch.liquid", templateContent);
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/dispatch.liquid";

        var result = ConfigurationValidator.Validate(config, _tempDir);

        result.Errors.Where(e => e.Contains("parse error")).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GenuinelyInvalidLiquid_StillFails()
    {
        CreateTemplateFile("templates/invalid.liquid", "{% if %}");
        var config = ValidConfig();
        config.Templates["entity"].Path = "templates/invalid.liquid";

        var result = ConfigurationValidator.Validate(config, _tempDir);
        result.Errors.ShouldContain(e => e.Contains("parse error"));
    }

    // --- Schema validation tests ---

    private static string ValidConfigJson() => """
        {
            "$schema": "./persistence.project.schema.json",
            "Connection": {
                "Provider": "sqlite",
                "ConnectionString": "Data Source=test.db"
            },
            "OutputDir": "src/Data",
            "Templates": {
                "entity": {
                    "Path": "templates/entity.liquid",
                    "OutputPattern": "Entities/{{entity.Name}}.cs",
                    "Scope": "PerModel",
                    "Mode": "Always"
                }
            },
            "Defaults": {
                "Namespace": "MyApp.Data",
                "Schema": "dbo"
            },
            "Logging": {
                "Enabled": false
            }
        }
        """;

    [Fact]
    public void SchemaValidate_ValidConfig_NoErrors()
    {
        var errors = ProjectConfigurationSchema.Validate(ValidConfigJson());
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void SchemaValidate_ProjectNameAndDescription_Accepted()
    {
        var json = """
            {
                "ProjectName": "connect-accounting",
                "Description": "Accounting database",
                "Connection": { "Provider": "sqlite", "ConnectionString": "x" },
                "OutputDir": "out",
                "Templates": {}
            }
            """;

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void SchemaValidate_UnknownRootProperty_ReportsError()
    {
        var json = """
            {
                "Connection": { "Provider": "sqlite", "ConnectionString": "x" },
                "OutputDir": "out",
                "Templates": {},
                "Bogus": "value"
            }
            """;

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("Bogus") || e.Contains("additional"));
    }

    [Fact]
    public void SchemaValidate_WrongRelationshipFieldNames_ReportsError()
    {
        var json = """
            {
                "Connection": { "Provider": "sqlite", "ConnectionString": "x" },
                "OutputDir": "out",
                "Templates": {},
                "Relationships": [
                    {
                        "Table": "Orders",
                        "Column": "CustomerId",
                        "PrincipalColumn": "Id"
                    }
                ]
            }
            """;

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("Table") || e.Contains("additional") || e.Contains("required"));
    }

    [Fact]
    public void SchemaValidate_MissingRequiredRelationshipFields_ReportsError()
    {
        var json = """
            {
                "Connection": { "Provider": "sqlite", "ConnectionString": "x" },
                "OutputDir": "out",
                "Templates": {},
                "Relationships": [
                    {
                        "DependentTable": "Orders"
                    }
                ]
            }
            """;

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("required") || e.Contains("DependentColumns") || e.Contains("PrincipalTable") || e.Contains("PrincipalColumns"));
    }

    [Fact]
    public void SchemaValidate_InvalidProviderEnum_ReportsError()
    {
        var json = """
            {
                "Connection": { "Provider": "oracle", "ConnectionString": "x" },
                "OutputDir": "out",
                "Templates": {}
            }
            """;

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("oracle") || e.Contains("enum") || e.Contains("Provider"));
    }

    [Fact]
    public void SchemaValidate_UnknownPropertyInConnection_ReportsError()
    {
        var json = """
            {
                "Connection": { "Provider": "sqlite", "ConnectionString": "x", "Timeout": 30 },
                "OutputDir": "out",
                "Templates": {}
            }
            """;

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("Timeout") || e.Contains("additional"));
    }

    [Fact]
    public void SchemaValidate_InvalidJsonSyntax_ReportsError()
    {
        var json = "{ not valid json }";

        var errors = ProjectConfigurationSchema.Validate(json);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("syntax"));
    }

    [Fact]
    public void SchemaValidate_CombinedOverload_MergesSchemaAndSemanticErrors()
    {
        CreateTemplateFile("templates/entity.liquid");
        var json = """
            {
                "Connection": { "Provider": "sqlite", "ConnectionString": "Data Source=test.db" },
                "OutputDir": "src/Data",
                "Templates": {
                    "entity": {
                        "Path": "templates/entity.liquid",
                        "OutputPattern": "out.cs",
                        "Scope": "PerModel",
                        "Mode": "Always"
                    }
                },
                "Bogus": "should fail schema"
            }
            """;

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig { Provider = "sqlite", ConnectionString = "Data Source=test.db" },
            OutputDir = "src/Data",
            Templates = new()
            {
                ["entity"] = new TemplateConfig
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "out.cs",
                    Scope = "PerModel",
                    Mode = "Always"
                }
            }
        };

        var result = ConfigurationValidator.Validate(json, config, _tempDir);
        result.Valid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("Schema") || e.Contains("Bogus") || e.Contains("additional"));
    }
}
