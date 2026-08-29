using Fluid;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;

namespace Pondhawk.Generation.Configuration;

public sealed class ValidationResult
{
    public bool Valid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

public static class ConfigurationValidator
{
    private static readonly HashSet<string> ValidScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PerItem", "Single"
    };

    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Always", "SkipExisting"
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
        ValidateCore(config, projectDir, result);
        return result;
    }

    public static ValidationResult Validate(string rawJson, ProjectConfiguration config, string projectDir)
    {
        var result = new ValidationResult();
        result.Errors.AddRange(ProjectConfigurationSchema.Validate(rawJson));
        ValidateCore(config, projectDir, result);
        return result;
    }

    private static void ValidateCore(ProjectConfiguration config, string projectDir, ValidationResult result)
    {
        var model = LoadModel(projectDir, result);

        ValidateRequiredSections(config, result);
        var macrosByTemplate = ValidateTemplates(config, projectDir, model, result);
        ValidateOverrides(config, model, macrosByTemplate, result);
        ValidateLogging(config, result);
        CheckUnresolvedEnvVars(config, result);
        CheckOutputPathCollisions(config, result);
    }

    /// <summary>
    /// Reads the input model so templates and overrides can be checked against the Kinds and
    /// paths that actually exist. A missing model is not an error — a project can be configured
    /// before its model is written — but a malformed one is.
    /// </summary>
    private static ModelFile? LoadModel(string projectDir, ValidationResult result)
    {
        var modelPath = Path.Combine(projectDir, "model.json");
        if (!File.Exists(modelPath))
            return null;

        try
        {
            var json = File.ReadAllText(modelPath);
            foreach (var error in ModelFileSchema.Validate(json))
                result.Errors.Add($"model.json: {error}");

            return ModelFileLoader.Deserialize(json);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"model.json: {ex.Message}");
            return null;
        }
    }

    private static void ValidateRequiredSections(ProjectConfiguration config, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(config.OutputDir))
            result.Errors.Add("Required field 'OutputDir' is missing or empty");

        if (config.Templates.Count == 0)
            result.Errors.Add("Required section 'Templates' is missing or empty");
    }

    /// <summary>Validates each template and returns the macro names each one declares.</summary>
    private static Dictionary<string, HashSet<string>> ValidateTemplates(
        ProjectConfiguration config, string projectDir, ModelFile? model, ValidationResult result)
    {
        var macrosByTemplate = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var parser = TemplateEngine.CreateParser();
        var kinds = model is null
            ? null
            : model.Nodes.Select(n => n.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);

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

                    foreach (var filterName in TemplateEngine.ValidateFilterNames(source))
                        result.Warnings.Add($"Template '{key}': Unknown filter '{filterName}' in '{template.Path}'");

                    macrosByTemplate[key] = TemplateEngine.ExtractMacroNames(source);
                }
            }

            if (string.IsNullOrWhiteSpace(template.OutputPattern))
                result.Errors.Add($"Template '{key}': 'OutputPattern' is required");

            if (string.IsNullOrWhiteSpace(template.Scope))
                result.Errors.Add($"Template '{key}': 'Scope' is required");
            else if (!ValidScopes.Contains(template.Scope))
                result.Errors.Add($"Template '{key}': Invalid scope '{template.Scope}'. Valid values: PerItem, Single");

            if (string.IsNullOrWhiteSpace(template.Mode))
                result.Errors.Add($"Template '{key}': 'Mode' is required");
            else if (!ValidModes.Contains(template.Mode))
                result.Errors.Add($"Template '{key}': Invalid mode '{template.Mode}'. Valid values: Always, SkipExisting");

            // AppliesTo names a node Kind, which the model defines rather than the engine, so it
            // can only be checked against the model that is actually present.
            if (!string.IsNullOrEmpty(template.AppliesTo)
                && !template.AppliesTo.Equals("All", StringComparison.OrdinalIgnoreCase)
                && kinds is { Count: > 0 }
                && !kinds.Contains(template.AppliesTo))
            {
                result.Warnings.Add(
                    $"Template '{key}': AppliesTo '{template.AppliesTo}' matches no top-level node Kind in model.json " +
                    $"(present: {string.Join(", ", kinds.Order())}) — this template will generate nothing");
            }
        }

        return macrosByTemplate;
    }

    private static void ValidateOverrides(
        ProjectConfiguration config,
        ModelFile? model,
        Dictionary<string, HashSet<string>> macrosByTemplate,
        ValidationResult result)
    {
        foreach (var ovr in config.Overrides)
        {
            if (string.IsNullOrWhiteSpace(ovr.Path))
            {
                result.Errors.Add("Override: 'Path' is required");
                continue;
            }

            if (!string.IsNullOrEmpty(ovr.Variant) && string.IsNullOrEmpty(ovr.Artifact))
                result.Errors.Add($"Override '{ovr.Path}': 'Artifact' is required when 'Variant' is specified");

            if (string.IsNullOrEmpty(ovr.Variant) && ovr.Metadata is not { Count: > 0 } && !ovr.Ignore)
                result.Errors.Add($"Override '{ovr.Path}': Must specify at least one of 'Variant', 'Metadata', or 'Ignore'");

            if (!string.IsNullOrEmpty(ovr.Artifact) && !config.Templates.ContainsKey(ovr.Artifact))
                result.Errors.Add($"Override '{ovr.Path}': Artifact '{ovr.Artifact}' is not a configured template");

            if (model is null)
                continue;

            var matched = model.Nodes
                .SelectMany(n => n.Descend())
                .Where(d => OverrideResolver.MatchesPath(ovr.Path, d.Path))
                .ToList();

            // A path matching nothing is almost always a typo, and silently generates the
            // unmodified artifact — the failure mode this tool exists to prevent.
            if (matched.Count == 0)
            {
                result.Warnings.Add($"Override '{ovr.Path}': matches no node in model.json");
                continue;
            }

            ValidateVariantMacroExists(ovr, matched, macrosByTemplate, result);
        }
    }

    /// <summary>
    /// Checks that the variant an override names resolves to a macro the artifact's template
    /// declares. Dispatch builds the macro name as {Variant}{Kind} and silently falls back to
    /// Default{Kind} when it is missing, so a misspelled variant renders the default and the
    /// generated file looks correct while ignoring the override entirely.
    /// </summary>
    private static void ValidateVariantMacroExists(
        OverrideConfig ovr,
        List<(Node Node, string Path)> matched,
        Dictionary<string, HashSet<string>> macrosByTemplate,
        ValidationResult result)
    {
        if (string.IsNullOrEmpty(ovr.Variant) || string.IsNullOrEmpty(ovr.Artifact))
            return;

        if (!macrosByTemplate.TryGetValue(ovr.Artifact, out var macros))
            return; // template unreadable — already reported

        foreach (var kind in matched.Select(m => m.Node.Kind).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var expected = $"{ovr.Variant}{kind}";
            if (macros.Contains(expected))
                continue;

            var suggestion = macros.FirstOrDefault(m => m.EndsWith(kind, StringComparison.OrdinalIgnoreCase)
                                                        && !m.StartsWith("Default", StringComparison.OrdinalIgnoreCase));

            result.Errors.Add(
                $"Override '{ovr.Path}': template '{ovr.Artifact}' declares no macro '{expected}', "
                + $"so matched {kind} nodes would silently render through 'Default{kind}' instead"
                + (suggestion is not null ? $". Did you mean '{suggestion}'?" : "."));
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
        var resolver = new EnvironmentResolver();

        foreach (var (key, value) in config.Values)
        {
            if (value is not string text) continue;
            var (_, unresolved) = resolver.TryResolve(text);
            foreach (var varName in unresolved)
                result.Warnings.Add($"Values.{key}: Unresolved environment variable '${{{varName}}}'");
        }
    }

    private static void CheckOutputPathCollisions(ProjectConfiguration config, ValidationResult result)
    {
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
