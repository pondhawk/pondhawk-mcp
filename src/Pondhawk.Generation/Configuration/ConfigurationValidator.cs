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
        var models = LoadModels(config, projectDir, result);

        ValidateRequiredSections(config, result);
        var partials = ValidatePartials(config, projectDir, result);
        var macrosByTemplate = ValidateTemplates(config, projectDir, models, partials, result);
        ValidateOverrides(config, models, macrosByTemplate, result);
        ValidateLogging(config, result);
        CheckUnresolvedEnvVars(config, result);
        CheckOutputPathCollisions(config, result);
    }

    /// <summary>
    /// Reads every model the templates reference, so each template and override can be checked
    /// against the Kinds and paths that exist in the model it actually renders. A missing model
    /// is not an error — a project can be configured before its model is written — but a
    /// malformed one is. Keyed by the file name as configured.
    /// </summary>
    private static Dictionary<string, ModelFile?> LoadModels(
        ProjectConfiguration config, string projectDir, ValidationResult result)
    {
        var models = new Dictionary<string, ModelFile?>(StringComparer.OrdinalIgnoreCase);

        // The default is always checked, even when no template names it: a model written before
        // the templates that will read it should still be validated.
        var referenced = config.Templates.Values
            .Select(t => t.ModelFile)
            .Append(TemplateConfig.DefaultModelFile)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var modelFile in referenced)
            models[modelFile] = LoadModel(modelFile, projectDir, result);

        return models;
    }

    private static ModelFile? LoadModel(string modelFile, string projectDir, ValidationResult result)
    {
        var modelPath = Path.Combine(projectDir, modelFile);
        if (!File.Exists(modelPath))
            return null;

        try
        {
            var json = File.ReadAllText(modelPath);
            foreach (var error in ModelFileSchema.Validate(json))
                result.Errors.Add($"{modelFile}: {error}");

            return ModelFileLoader.Deserialize(json);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{modelFile}: {ex.Message}");
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

    /// <summary>
    /// Validates the shared macro files in their own right and returns their sources, in order.
    /// </summary>
    /// <remarks>
    /// Each partial is parsed on its own rather than as part of a composed document, so a syntax
    /// error is reported against the file it is in, at its real line number, instead of being
    /// attributed to every template that shares it at an offset.
    /// </remarks>
    private static List<string> ValidatePartials(
        ProjectConfiguration config, string projectDir, ValidationResult result)
    {
        var sources = new List<string>();
        var parser = TemplateEngine.CreateParser();

        foreach (var partial in config.Partials)
        {
            string fullPath;
            try
            {
                fullPath = FileWriter.ResolveContained(projectDir, partial);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Partial '{partial}': {ex.Message}");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                result.Errors.Add($"Partial '{partial}': File not found");
                continue;
            }

            var source = File.ReadAllText(fullPath);
            sources.Add(source);

            if (!parser.TryParse(source, out _, out var error))
                result.Errors.Add($"Partial '{partial}': Liquid parse error: {error}");

            foreach (var filterName in TemplateEngine.ValidateFilterNames(source))
                result.Warnings.Add($"Partial '{partial}': Unknown filter '{filterName}'");
        }

        return sources;
    }

    /// <summary>Validates each template and returns the macro names each one declares.</summary>
    private static Dictionary<string, HashSet<string>> ValidateTemplates(
        ProjectConfiguration config, string projectDir, Dictionary<string, ModelFile?> models,
        List<string> partials, ValidationResult result)
    {
        var macrosByTemplate = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var parser = TemplateEngine.CreateParser();

        foreach (var (key, template) in config.Templates)
        {
            // Each template is checked against the model it actually renders, not against
            // whatever else the project happens to contain.
            var modelFile = template.ModelFile;
            var model = models.GetValueOrDefault(modelFile);
            var kinds = model?.Nodes.Select(n => n.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // A named model that is not there is far likelier to be a typo than a file not yet
            // written, so say so — generate would fail on it.
            if (model is null
                && !modelFile.Equals(TemplateConfig.DefaultModelFile, StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(
                    $"Template '{key}': model '{modelFile}' does not exist — this template will fail to generate");
            }

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

                    // Macros are extracted from the composed source, because a variant macro
                    // living in a shared partial is just as available to dispatch as one in the
                    // template. Extracting from this file alone would report a correct project
                    // as declaring no such macro — a false error on the check that exists to
                    // catch a silently-ignored override.
                    var composed = TemplateComposer.Compose(partials, source);
                    macrosByTemplate[key] = TemplateEngine.ExtractMacroNames(composed);

                    WarnUndispatchableKinds(key, template, composed, macrosByTemplate[key], model, modelFile, result);
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
                    $"Template '{key}': AppliesTo '{template.AppliesTo}' matches no top-level node Kind in {modelFile} " +
                    $"(present: {string.Join(", ", kinds.Order())}) — this template will generate nothing");
            }
        }

        return macrosByTemplate;
    }

    /// <summary>
    /// Warns when a Kind nested under the nodes a template renders has no macro to render it.
    /// </summary>
    /// <remarks>
    /// Dispatch is a runtime lookup, so which nodes a template actually reaches cannot be known
    /// statically. This is therefore advisory and deliberately conservative: it says nothing
    /// about a template that never dispatches, nothing about the top-level Kinds a template
    /// usually renders in its own body, and nothing at all when there is no model to check
    /// against. Missing a case is cheap — since dispatch fails the file outright, `generate`
    /// will say so plainly — whereas a warning that cries wolf gets the whole list skipped.
    /// </remarks>
    private static void WarnUndispatchableKinds(
        string key, TemplateConfig template, string composedSource, HashSet<string> macros,
        ModelFile? model, string modelFile, ValidationResult result)
    {
        if (model is null || !TemplateEngine.UsesDispatch(composedSource))
            return;

        var reachable = model.Nodes
            .Where(n => MatchesAppliesTo(n, template.AppliesTo))
            .SelectMany(n => n.Descend())
            .Where(d => d.Path.Contains('/'))  // the root itself is bound as item, not dispatched
            .GroupBy(d => d.Node.Kind, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in reachable)
        {
            if (macros.Contains($"Default{group.Key}"))
                continue;

            result.Warnings.Add(
                $"Template '{key}': dispatches, but declares no macro 'Default{group.Key}' — "
                + $"{modelFile} has {group.Count()} '{group.Key}' node(s) beneath the nodes this template renders. "
                + "Dispatching one fails the file.");
        }
    }

    private static bool MatchesAppliesTo(Node node, string? appliesTo)
        => string.IsNullOrEmpty(appliesTo)
           || appliesTo.Equals("All", StringComparison.OrdinalIgnoreCase)
           || appliesTo.Equals(node.Kind, StringComparison.OrdinalIgnoreCase);

    private static void ValidateOverrides(
        ProjectConfiguration config,
        Dictionary<string, ModelFile?> models,
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

            // An override scoped to an artifact is only ever applied to that template's model.
            // An unscoped one applies to every template, so it is enough for it to match in any
            // of them — searching only the default model would report false typos.
            var searched = ScopedModels(config, models, ovr.Artifact);

            var matched = searched
                .Where(m => m.Model is not null)
                .SelectMany(m => m.Model!.Nodes.SelectMany(n => n.Descend()))
                .Where(d => OverrideResolver.MatchesPath(ovr.Path, d.Path))
                .ToList();

            if (searched.All(m => m.Model is null))
                continue;

            // A path matching nothing is almost always a typo, and silently generates the
            // unmodified artifact — the failure mode this tool exists to prevent.
            if (matched.Count == 0)
            {
                result.Warnings.Add($"Override '{ovr.Path}': matches no node in {DescribeScope(searched)}");
                continue;
            }

            ValidateVariantMacroExists(ovr, matched, macrosByTemplate, result);
        }
    }

    /// <summary>
    /// The models an override can possibly apply to: one when it names an artifact, all of them
    /// when it does not.
    /// </summary>
    private static List<(string File, ModelFile? Model)> ScopedModels(
        ProjectConfiguration config, Dictionary<string, ModelFile?> models, string? artifact)
    {
        if (!string.IsNullOrEmpty(artifact) && config.Templates.TryGetValue(artifact, out var template))
        {
            var file = template.ModelFile;
            return [(file, models.GetValueOrDefault(file))];
        }

        return models.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    private static string DescribeScope(List<(string File, ModelFile? Model)> searched)
    {
        var present = searched.Where(m => m.Model is not null).Select(m => m.File).Order().ToList();
        return present.Count == 1
            ? present[0]
            : $"any model ({string.Join(", ", present)})";
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
