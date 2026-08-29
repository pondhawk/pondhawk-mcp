using Fluid;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;

namespace Pondhawk.Generation.Caching;

public sealed class TimestampCache
{
    private readonly TemplateEngine _templateEngine;

    private string? _configPath;
    private DateTime _configTimestamp;
    private ProjectConfiguration? _cachedConfig;

    private readonly Dictionary<string, DateTime> _templateTimestamps = new();
    private readonly Dictionary<string, IFluidTemplate> _compiledTemplates = new();

    private string? _modelPath;
    private DateTime _modelTimestamp;
    private ModelFile? _cachedModel;

    public TimestampCache(TemplateEngine templateEngine)
    {
        _templateEngine = templateEngine;
    }

    /// <summary>
    /// Gets the project configuration, reloading from disk if the file has been modified.
    /// </summary>
    public ProjectConfiguration GetConfiguration(string configPath)
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

    /// <summary>
    /// Gets a compiled template, recompiling from disk if the file has been modified.
    /// </summary>
    public IFluidTemplate GetTemplate(string templatePath)
    {
        var currentTimestamp = File.GetLastWriteTimeUtc(templatePath);

        if (_compiledTemplates.TryGetValue(templatePath, out var cached) &&
            _templateTimestamps.TryGetValue(templatePath, out var cachedTs) &&
            cachedTs == currentTimestamp)
        {
            return cached;
        }

        var source = File.ReadAllText(templatePath);
        if (!_templateEngine.TryParse(source, out var template, out var error))
        {
            throw new InvalidOperationException($"Failed to parse template '{templatePath}': {error}");
        }

        _templateTimestamps[templatePath] = currentTimestamp;
        _compiledTemplates[templatePath] = template;
        return template;
    }

    /// <summary>
    /// Gets the parsed input model, reloading if the file has been modified.
    /// Returns null when no model file exists yet.
    /// </summary>
    public ModelFile? GetModel(string modelPath)
    {
        if (!File.Exists(modelPath))
            return null;

        var currentTimestamp = File.GetLastWriteTimeUtc(modelPath);

        if (_cachedModel is not null && _modelPath == modelPath && _modelTimestamp == currentTimestamp)
        {
            return _cachedModel;
        }

        _cachedModel = ModelFileLoader.Load(modelPath);
        _modelPath = modelPath;
        _modelTimestamp = currentTimestamp;
        return _cachedModel;
    }

    /// <summary>
    /// Invalidates all caches (config, templates, model).
    /// </summary>
    public void InvalidateAll()
    {
        _cachedConfig = null;
        _configPath = null;
        _configTimestamp = default;
        _templateTimestamps.Clear();
        _compiledTemplates.Clear();
        _cachedModel = null;
        _modelPath = null;
        _modelTimestamp = default;
    }

    /// <summary>
    /// Invalidates a single template's cache.
    /// </summary>
    public void InvalidateTemplate(string templatePath)
    {
        _templateTimestamps.Remove(templatePath);
        _compiledTemplates.Remove(templatePath);
    }

    /// <summary>
    /// Checks if the config file has changed since last load.
    /// Returns true if stale (needs reload).
    /// </summary>
    public bool IsConfigStale(string configPath)
    {
        if (_cachedConfig is null || _configPath != configPath)
            return true;

        var currentTimestamp = File.GetLastWriteTimeUtc(configPath);
        return _configTimestamp != currentTimestamp;
    }

    /// <summary>
    /// Checks if a template file has changed since last compilation.
    /// Returns true if stale (needs recompilation).
    /// </summary>
    public bool IsTemplateStale(string templatePath)
    {
        if (!_templateTimestamps.TryGetValue(templatePath, out var cachedTs))
            return true;

        var currentTimestamp = File.GetLastWriteTimeUtc(templatePath);
        return cachedTs != currentTimestamp;
    }

    /// <summary>
    /// Returns whether an input model file exists.
    /// </summary>
    public bool HasModel(string modelPath) => File.Exists(modelPath);
}
