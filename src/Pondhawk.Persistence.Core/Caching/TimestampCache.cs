using Fluid;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Rendering;

namespace Pondhawk.Persistence.Core.Caching;

public sealed class TimestampCache
{
    private readonly TemplateEngine _templateEngine;

    private string? _configPath;
    private DateTime _configTimestamp;
    private ProjectConfiguration? _cachedConfig;

    private readonly Dictionary<string, DateTime> _templateTimestamps = new();
    private readonly Dictionary<string, IFluidTemplate> _compiledTemplates = new();

    private string? _schemaPath;
    private DateTime _schemaTimestamp;
    private List<Models.Model>? _cachedSchema;

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

        // Config changed or first load — invalidate config and templates (not schema)
        InvalidateConfigAndTemplates();

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
    /// Gets cached schema from the db-design.json file, reloading if the file has been modified.
    /// Returns null if the schema file does not exist.
    /// </summary>
    public List<Models.Model>? GetSchema(string schemaPath)
    {
        if (!File.Exists(schemaPath))
            return null;

        var currentTimestamp = File.GetLastWriteTimeUtc(schemaPath);

        if (_cachedSchema is not null && _schemaPath == schemaPath && _schemaTimestamp == currentTimestamp)
        {
            return _cachedSchema;
        }

        var json = File.ReadAllText(schemaPath);
        var schemaFile = SchemaFileMapper.Deserialize(json);
        _cachedSchema = SchemaFileMapper.ToModels(schemaFile);
        _schemaPath = schemaPath;
        _schemaTimestamp = currentTimestamp;
        return _cachedSchema;
    }

    /// <summary>
    /// Gets the SchemaFile metadata (Database, Provider) from the cached schema file.
    /// Returns null if the schema file does not exist.
    /// </summary>
    public SchemaFile? GetSchemaFile(string schemaPath)
    {
        if (!File.Exists(schemaPath))
            return null;

        var json = File.ReadAllText(schemaPath);
        return SchemaFileMapper.Deserialize(json);
    }

    /// <summary>
    /// Writes schema to disk and updates the in-memory cache.
    /// </summary>
    public void SetSchema(List<Models.Model> models, string schemaPath, string database, string provider, string? origin = null)
    {
        var schemaFile = SchemaFileMapper.ToSchemaFile(models, database, provider, origin: origin, schema_: "./db-design.schema.json");
        var json = SchemaFileMapper.Serialize(schemaFile);

        var dir = Path.GetDirectoryName(schemaPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(schemaPath, json, new System.Text.UTF8Encoding(false));

        _cachedSchema = models;
        _schemaPath = schemaPath;
        _schemaTimestamp = File.GetLastWriteTimeUtc(schemaPath);
    }

    /// <summary>
    /// Updates the config timestamp after a TypeMappings write-back
    /// without invalidating the schema cache.
    /// </summary>
    public void UpdateConfigTimestampAfterWriteBack(string configPath)
    {
        _configTimestamp = File.GetLastWriteTimeUtc(configPath);
        _configPath = configPath;
        // Reload the config but do NOT invalidate schema
        _cachedConfig = ProjectConfigurationLoader.Load(configPath);
        // Template cache stays intact too — only the config content changed
    }

    /// <summary>
    /// Invalidates all caches (config, templates, schema).
    /// </summary>
    public void InvalidateAll()
    {
        _cachedConfig = null;
        _configPath = null;
        _configTimestamp = default;
        _templateTimestamps.Clear();
        _compiledTemplates.Clear();
        _cachedSchema = null;
        _schemaPath = null;
        _schemaTimestamp = default;
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
    /// Returns whether there is cached schema data.
    /// </summary>
    public bool HasSchema(string schemaPath) => File.Exists(schemaPath);

    /// <summary>
    /// Invalidates config and template caches but preserves schema cache.
    /// </summary>
    private void InvalidateConfigAndTemplates()
    {
        _cachedConfig = null;
        _configPath = null;
        _configTimestamp = default;
        _templateTimestamps.Clear();
        _compiledTemplates.Clear();
    }
}
