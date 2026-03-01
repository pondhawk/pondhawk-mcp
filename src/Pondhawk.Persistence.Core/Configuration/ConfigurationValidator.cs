using Fluid;
using Pondhawk.Persistence.Core.Rendering;

namespace Pondhawk.Persistence.Core.Configuration;

public sealed class ValidationResult
{
    public bool Valid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

public static class ConfigurationValidator
{
    private static readonly HashSet<string> ValidProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlserver", "postgresql", "mysql", "mariadb", "sqlite"
    };

    private static readonly HashSet<string> ValidScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PerModel", "SingleFile"
    };

    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Always", "SkipExisting"
    };

    private static readonly HashSet<string> ValidAppliesTo = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tables", "Views", "All"
    };

    private static readonly HashSet<string> ValidLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Verbose", "Debug", "Information", "Warning", "Error", "Fatal"
    };

    private static readonly HashSet<string> ValidRollingIntervals = new(StringComparer.OrdinalIgnoreCase)
    {
        "Infinite", "Year", "Month", "Day", "Hour", "Minute"
    };

    public static ValidationResult Validate(ProjectConfiguration config, string projectDir)
    {
        var result = new ValidationResult();

        ValidateRequiredSections(config, result);
        ValidateConnections(config, result);
        ValidateTemplates(config, projectDir, result);
        ValidateDataTypeReferences(config, result);
        ValidateRelationships(config, result);
        ValidateOverrides(config, result);
        ValidateLogging(config, result);
        CheckUnresolvedEnvVars(config, result);
        CheckOutputPathCollisions(config, result);

        return result;
    }

    public static ValidationResult Validate(string rawJson, ProjectConfiguration config, string projectDir)
    {
        var result = new ValidationResult();

        var schemaErrors = ProjectConfigurationSchema.Validate(rawJson);
        result.Errors.AddRange(schemaErrors);

        ValidateRequiredSections(config, result);
        ValidateConnections(config, result);
        ValidateTemplates(config, projectDir, result);
        ValidateDataTypeReferences(config, result);
        ValidateRelationships(config, result);
        ValidateOverrides(config, result);
        ValidateLogging(config, result);
        CheckUnresolvedEnvVars(config, result);
        CheckOutputPathCollisions(config, result);

        // Validate db-design.json if it exists
        var dbDesignPath = Path.Combine(projectDir, "db-design.json");
        if (File.Exists(dbDesignPath))
        {
            var dbDesignJson = File.ReadAllText(dbDesignPath);
            var dbDesignErrors = DbDesignFileSchema.Validate(dbDesignJson);
            result.Errors.AddRange(dbDesignErrors);
        }

        return result;
    }

    private static void ValidateRequiredSections(ProjectConfiguration config, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(config.Connection.Provider) && string.IsNullOrWhiteSpace(config.Connection.ConnectionString))
            result.Errors.Add("Required section 'Connection' is missing or empty");

        if (string.IsNullOrWhiteSpace(config.OutputDir))
            result.Errors.Add("Required field 'OutputDir' is missing or empty");

        if (config.Templates.Count == 0)
            result.Errors.Add("Required section 'Templates' is missing or empty");
    }

    private static void ValidateConnections(ProjectConfiguration config, ValidationResult result)
    {
        var conn = config.Connection;
        if (string.IsNullOrWhiteSpace(conn.Provider) && string.IsNullOrWhiteSpace(conn.ConnectionString))
            return; // Already reported by ValidateRequiredSections

        if (string.IsNullOrWhiteSpace(conn.Provider))
            result.Errors.Add("Connection: 'Provider' is required");
        else if (!ValidProviders.Contains(conn.Provider))
            result.Errors.Add($"Connection: Invalid provider '{conn.Provider}'. Valid values: {string.Join(", ", ValidProviders)}");

        if (string.IsNullOrWhiteSpace(conn.ConnectionString))
            result.Errors.Add("Connection: 'ConnectionString' is required");
    }

    private static void ValidateTemplates(ProjectConfiguration config, string projectDir, ValidationResult result)
    {
        var parser = TemplateEngine.CreateParser();

        foreach (var (key, template) in config.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Path))
                result.Errors.Add($"Template '{key}': 'Path' is required");
            else
            {
                var fullPath = Path.Combine(projectDir, template.Path);
                if (!File.Exists(fullPath))
                    result.Errors.Add($"Template '{key}': File not found at '{template.Path}'");
                else
                {
                    var source = File.ReadAllText(fullPath);
                    if (!parser.TryParse(source, out _, out var error))
                        result.Errors.Add($"Template '{key}': Liquid parse error in '{template.Path}': {error}");

                    // Check for unknown filter names
                    var unknownFilters = TemplateEngine.ValidateFilterNames(source);
                    foreach (var filterName in unknownFilters)
                        result.Warnings.Add($"Template '{key}': Unknown filter '{filterName}' in '{template.Path}'");
                }
            }

            if (string.IsNullOrWhiteSpace(template.OutputPattern))
                result.Errors.Add($"Template '{key}': 'OutputPattern' is required");

            if (string.IsNullOrWhiteSpace(template.Scope))
                result.Errors.Add($"Template '{key}': 'Scope' is required");
            else if (!ValidScopes.Contains(template.Scope))
                result.Errors.Add($"Template '{key}': Invalid scope '{template.Scope}'. Valid values: PerModel, SingleFile");

            if (string.IsNullOrWhiteSpace(template.Mode))
                result.Errors.Add($"Template '{key}': 'Mode' is required");
            else if (!ValidModes.Contains(template.Mode))
                result.Errors.Add($"Template '{key}': Invalid mode '{template.Mode}'. Valid values: Always, SkipExisting");

            if (!string.IsNullOrEmpty(template.AppliesTo) && !ValidAppliesTo.Contains(template.AppliesTo))
                result.Errors.Add($"Template '{key}': Invalid AppliesTo '{template.AppliesTo}'. Valid values: Tables, Views, All");
        }
    }

    private static void ValidateDataTypeReferences(ProjectConfiguration config, ValidationResult result)
    {
        foreach (var mapping in config.TypeMappings)
        {
            if (!string.IsNullOrEmpty(mapping.DataType) && !config.DataTypes.ContainsKey(mapping.DataType))
                result.Errors.Add($"TypeMapping for '{mapping.DbType}': DataType '{mapping.DataType}' is not defined in DataTypes");
        }
    }

    private static void ValidateRelationships(ProjectConfiguration config, ValidationResult result)
    {
        foreach (var rel in config.Relationships)
        {
            if (string.IsNullOrWhiteSpace(rel.DependentTable))
                result.Errors.Add("Relationship: 'DependentTable' is required");

            if (rel.DependentColumns.Count == 0)
                result.Errors.Add($"Relationship on '{rel.DependentTable}': 'DependentColumns' is required");

            if (string.IsNullOrWhiteSpace(rel.PrincipalTable))
                result.Errors.Add($"Relationship on '{rel.DependentTable}': 'PrincipalTable' is required");

            if (rel.PrincipalColumns.Count == 0)
                result.Errors.Add($"Relationship on '{rel.DependentTable}': 'PrincipalColumns' is required");
        }
    }

    private static void ValidateOverrides(ProjectConfiguration config, ValidationResult result)
    {
        foreach (var ovr in config.Overrides)
        {
            if (string.IsNullOrWhiteSpace(ovr.Class))
                result.Errors.Add("Override: 'Class' is required");

            if (!string.IsNullOrEmpty(ovr.DataType) && !config.DataTypes.ContainsKey(ovr.DataType))
                result.Errors.Add($"Override for '{ovr.Class}.{ovr.Property ?? "*"}': DataType '{ovr.DataType}' is not defined in DataTypes");

            if (!string.IsNullOrEmpty(ovr.Variant) && string.IsNullOrEmpty(ovr.Artifact))
                result.Errors.Add($"Override for '{ovr.Class}.{ovr.Property ?? "*"}': 'Artifact' is required when 'Variant' is specified");

            if (string.IsNullOrEmpty(ovr.Variant) && string.IsNullOrEmpty(ovr.DataType) && !ovr.Ignore)
                result.Errors.Add($"Override for '{ovr.Class}.{ovr.Property ?? "*"}': Must specify at least one of 'Variant', 'DataType', or 'Ignore'");
        }
    }

    private static void ValidateLogging(ProjectConfiguration config, ValidationResult result)
    {
        if (!ValidLogLevels.Contains(config.Logging.Level))
            result.Errors.Add($"Logging: Invalid level '{config.Logging.Level}'. Valid values: {string.Join(", ", ValidLogLevels)}");

        if (!ValidRollingIntervals.Contains(config.Logging.RollingInterval))
            result.Errors.Add($"Logging: Invalid rolling interval '{config.Logging.RollingInterval}'. Valid values: {string.Join(", ", ValidRollingIntervals)}");
    }

    private static void CheckUnresolvedEnvVars(ProjectConfiguration config, ValidationResult result)
    {
        // Only connection strings support ${VAR} substitution (for keeping credentials out of git).
        var resolver = new EnvironmentResolver();

        var (_, unresolved) = resolver.TryResolve(config.Connection.ConnectionString);
        foreach (var varName in unresolved)
            result.Warnings.Add($"Connection: Unresolved environment variable '${{{varName}}}'");
    }

    private static void CheckOutputPathCollisions(ProjectConfiguration config, ValidationResult result)
    {
        // Simple check: warn if two templates with the same scope and output pattern exist
        var seen = new Dictionary<string, string>();
        foreach (var (key, template) in config.Templates)
        {
            var signature = $"{template.Scope}:{template.OutputPattern}";
            if (seen.TryGetValue(signature, out var existingKey))
                result.Warnings.Add($"Output path collision: '{template.OutputPattern}' produced by both '{existingKey}' and '{key}' templates");
            else
                seen[signature] = key;
        }
    }
}
