using System.Diagnostics;
using Pondhawk.Persistence.Core.Caching;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Logging;
using Pondhawk.Persistence.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace Pondhawk.Persistence.Mcp;

/// <summary>
/// Shared server state — project path, cache, logging, template engine.
/// Created once at startup and passed to tools via DI.
/// </summary>
public sealed class ServerContext : IDisposable
{
    public string ProjectDir { get; }
    public string ConfigPath => Path.Combine(ProjectDir, "persistence.project.json");
    public string SchemaPath => Path.Combine(ProjectDir, "db-design.json");
    public TemplateEngine TemplateEngine { get; } = new();
    public TimestampCache Cache { get; }
    public LoggingService LoggingService { get; } = new();
    public ILoggerFactory? LoggerFactory { get; private set; }

    public ServerContext(string projectDir)
    {
        ProjectDir = Path.GetFullPath(projectDir);
        Cache = new TimestampCache(TemplateEngine);
    }

    /// <summary>
    /// Initializes logging early at startup (before the host builds).
    /// If config exists, uses its logging settings; otherwise uses no-op.
    /// </summary>
    public void InitializeLogging()
    {
        LoggingConfig? loggingConfig = null;
        if (File.Exists(ConfigPath))
        {
            try
            {
                var config = Cache.GetConfiguration(ConfigPath);
                loggingConfig = config.Logging;
            }
            catch (Exception ex)
            {
                // Config may be malformed; proceed with no-op logging.
                // Can't log here — logger isn't initialized yet. Write to stderr as a fallback.
                Console.Error.WriteLine($"[pondhawk-mcp] Warning: failed to read config for logging setup: {ex.Message}");
            }
        }

        LoggerFactory = LoggingService.Initialize(loggingConfig, ProjectDir);
    }

    /// <summary>
    /// Loads (or reloads) config from cache, re-initializes logging if config changed.
    /// Call before each tool invocation.
    /// </summary>
    public ProjectConfiguration EnsureConfig()
    {
        var configBefore = _lastLoggingEnabled;
        var config = Cache.GetConfiguration(ConfigPath);

        // Re-initialize logging only if the logging config has changed
        var loggingChanged = _lastLoggingEnabled != config.Logging.Enabled
            || _lastLoggingLevel != config.Logging.Level
            || _lastLoggingPath != config.Logging.LogPath;

        if (LoggerFactory is null || loggingChanged)
        {
            LoggerFactory = LoggingService.Initialize(config.Logging, ProjectDir);
            _lastLoggingEnabled = config.Logging.Enabled;
            _lastLoggingLevel = config.Logging.Level;
            _lastLoggingPath = config.Logging.LogPath;
        }

        return config;
    }

    private bool _lastLoggingEnabled;
    private string _lastLoggingLevel = "";
    private string _lastLoggingPath = "";

    /// <summary>
    /// Resolves connection strings using .env file and system environment variables.
    /// Returns a copy of the config with resolved connection strings.
    /// </summary>
    public ProjectConfiguration ResolveConfig(ProjectConfiguration config)
    {
        var resolver = new EnvironmentResolver();
        var envPath = Path.Combine(ProjectDir, ".env");
        resolver.LoadEnvFile(envPath);
        resolver.ResolveConfiguration(config);
        return config;
    }

    /// <summary>
    /// Creates a logger for a tool. Returns a Stopwatch started at call time for duration tracking.
    /// </summary>
    public (ILogger Logger, Stopwatch Stopwatch) StartToolCall(string toolName, string? parameters = null)
    {
        var logger = LoggerFactory?.CreateLogger(toolName) ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger(toolName);
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Tool {ToolName} called{Parameters}", toolName, parameters is not null ? $" with {parameters}" : "");
        return (logger, sw);
    }

    public void Dispose()
    {
        LoggingService.Dispose();
    }
}
