using Fluid;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;

namespace Pondhawk.Generation.Caching;

public sealed class TimestampCache
{
    // An MCP server may handle tool calls concurrently, and every accessor here both reads
    // and mutates cached state. Monitor is re-entrant, so GetConfiguration calling
    // InvalidateAll while holding the gate is fine.
    private readonly object _gate = new();

    private readonly TemplateEngine _templateEngine;

    private string? _configPath;
    private DateTime _configTimestamp;
    private ProjectConfiguration? _cachedConfig;

    private readonly Dictionary<string, string> _templateSignatures = new();
    private readonly Dictionary<string, IFluidTemplate> _compiledTemplates = new();

    // Keyed by path: a project can declare several models, and switching between them on
    // consecutive templates in one generate run must not evict the other.
    private readonly Dictionary<string, DateTime> _modelTimestamps = new();
    private readonly Dictionary<string, ModelFile> _cachedModels = new();

    public TimestampCache(TemplateEngine templateEngine)
    {
        _templateEngine = templateEngine;
    }

    /// <summary>
    /// Gets the project configuration, reloading from disk if the file has been modified.
    /// </summary>
    public ProjectConfiguration GetConfiguration(string configPath)
    {
        lock (_gate)
        {
            var currentTimestamp = File.GetLastWriteTimeUtc(configPath);

            if (_cachedConfig is not null && _configPath == configPath && _configTimestamp == currentTimestamp)
            {
                return _cachedConfig;
            }

            // Config changed or first load — invalidate config, templates, and model. The model
            // must go too: overrides from config are applied to node metadata, so a cached tree
            // would carry rules that the edited config no longer declares.
            InvalidateAll();

            _configPath = configPath;
            _configTimestamp = currentTimestamp;
            _cachedConfig = ProjectConfigurationLoader.Load(configPath);
            return _cachedConfig;
    }
    }

    /// <summary>
    /// Gets a compiled template, recompiling when the file — or any partial composed into
    /// it — has been modified.
    /// </summary>
    /// <remarks>
    /// A template's cached form depends on every file that contributes to it. Keying only on
    /// the template's own timestamp would serve a stale compilation after a shared macro was
    /// edited, so the first generate after that change would silently render the old macro.
    /// </remarks>
    public IFluidTemplate GetTemplate(string templatePath, IReadOnlyList<string>? partialPaths = null)
    {
        lock (_gate)
        {
            partialPaths ??= [];
            var signature = InputSignature(templatePath, partialPaths);

            if (_compiledTemplates.TryGetValue(templatePath, out var cached) &&
                _templateSignatures.TryGetValue(templatePath, out var cachedSignature) &&
                cachedSignature == signature)
            {
                return cached;
            }

            var partialSources = partialPaths.Select(File.ReadAllText).ToList();
            var source = TemplateComposer.Compose(partialSources, File.ReadAllText(templatePath));

            if (!_templateEngine.TryParse(source, out var template, out var error))
            {
                throw new InvalidOperationException(
                    $"Failed to parse template '{Blame(templatePath, partialPaths, partialSources)}': {error}");
            }

            _templateSignatures[templatePath] = signature;
            _compiledTemplates[templatePath] = template;
            return template;
    }
    }

    /// <summary>
    /// Identifies the exact inputs a cached compilation was built from. A composite rather than
    /// a max timestamp, so a partial being added, removed, reordered or reverted to an older
    /// modification time all invalidate correctly.
    /// </summary>
    private static string InputSignature(string templatePath, IReadOnlyList<string> partialPaths)
    {
        var parts = partialPaths
            .Append(templatePath)
            .Select(path => $"{path}@{File.GetLastWriteTimeUtc(path).Ticks}");

        return string.Join("|", parts);
    }

    /// <summary>
    /// Names the file a parse error is actually in. The composed source is parsed as one
    /// document, so a broken partial would otherwise be reported against every template that
    /// shares it.
    /// </summary>
    private string Blame(string templatePath, IReadOnlyList<string> partialPaths, List<string> partialSources)
    {
        for (var i = 0; i < partialPaths.Count; i++)
            if (!_templateEngine.TryParse(partialSources[i], out _, out _))
                return partialPaths[i];

        return templatePath;
    }

    /// <summary>
    /// Gets the parsed input model, reloading if the file has been modified.
    /// Returns null when no model file exists yet.
    /// </summary>
    public ModelFile? GetModel(string modelPath)
    {
        lock (_gate)
        {
            if (!File.Exists(modelPath))
                return null;

            var currentTimestamp = File.GetLastWriteTimeUtc(modelPath);

            if (_cachedModels.TryGetValue(modelPath, out var cached) &&
                _modelTimestamps.TryGetValue(modelPath, out var cachedTs) &&
                cachedTs == currentTimestamp)
            {
                return cached;
            }

            var model = ModelFileLoader.Load(modelPath);
            _cachedModels[modelPath] = model;
            _modelTimestamps[modelPath] = currentTimestamp;
            return model;
    }
    }

    /// <summary>
    /// Invalidates all caches (config, templates, model).
    /// </summary>
    public void InvalidateAll()
    {
        lock (_gate)
        {
            _cachedConfig = null;
            _configPath = null;
            _configTimestamp = default;
            _templateSignatures.Clear();
            _compiledTemplates.Clear();
            _cachedModels.Clear();
            _modelTimestamps.Clear();
    }
    }

    /// <summary>
    /// Invalidates a single template's cache.
    /// </summary>
    public void InvalidateTemplate(string templatePath)
    {
        lock (_gate)
        {
            _templateSignatures.Remove(templatePath);
            _compiledTemplates.Remove(templatePath);
    }
    }

    /// <summary>
    /// Checks if the config file has changed since last load.
    /// Returns true if stale (needs reload).
    /// </summary>
    public bool IsConfigStale(string configPath)
    {
        lock (_gate)
        {
            if (_cachedConfig is null || _configPath != configPath)
                return true;

            var currentTimestamp = File.GetLastWriteTimeUtc(configPath);
            return _configTimestamp != currentTimestamp;
    }
    }

    /// <summary>
    /// Checks if a template file has changed since last compilation.
    /// Returns true if stale (needs recompilation).
    /// </summary>
    public bool IsTemplateStale(string templatePath, IReadOnlyList<string>? partialPaths = null)
    {
        lock (_gate)
        {
            if (!_templateSignatures.TryGetValue(templatePath, out var cachedSignature))
                return true;

            return cachedSignature != InputSignature(templatePath, partialPaths ?? []);
    }
    }

    /// <summary>
    /// Returns whether an input model file exists.
    /// </summary>
    public bool HasModel(string modelPath) => File.Exists(modelPath);
}
