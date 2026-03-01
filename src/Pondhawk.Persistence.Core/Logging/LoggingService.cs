using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pondhawk.Persistence.Core.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Pondhawk.Persistence.Core.Logging;

public sealed partial class LoggingService : IDisposable
{
    private Serilog.Core.Logger? _serilogLogger;
    private ILoggerFactory? _loggerFactory;

    /// <summary>
    /// The underlying Serilog logger. Exposed for host-level DI registration.
    /// </summary>
    public Serilog.ILogger? SerilogLogger => _serilogLogger;

    [GeneratedRegex(@"(ConnectionString\s*[=:]\s*)([^;""}\s]+[^""}\s]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPattern();

    /// <summary>
    /// Initializes logging based on the configuration.
    /// When Enabled=false, configures a no-op logger.
    /// </summary>
    public ILoggerFactory Initialize(LoggingConfig? config, string projectDir)
    {
        Dispose();

        config ??= new LoggingConfig();

        if (!config.Enabled)
        {
            _serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Fatal()
                .CreateLogger();

            _loggerFactory = new SerilogLoggerFactory(_serilogLogger);
            return _loggerFactory;
        }

        var logPath = Path.IsPathRooted(config.LogPath)
            ? config.LogPath
            : Path.Combine(projectDir, config.LogPath);

        // Ensure log directory exists
        var logDir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDir))
            Directory.CreateDirectory(logDir);

        var level = ParseLevel(config.Level);
        var interval = ParseRollingInterval(config.RollingInterval);
        var retainedCount = config.RetainedFileCountLimit == 0
            ? (int?)null
            : config.RetainedFileCountLimit;

        _serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                logPath,
                rollingInterval: interval,
                retainedFileCountLimit: retainedCount,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _loggerFactory = new SerilogLoggerFactory(_serilogLogger);
        return _loggerFactory;
    }

    /// <summary>
    /// Redacts connection strings from a message.
    /// Replaces ConnectionString values with "[REDACTED]".
    /// </summary>
    public static string RedactConnectionStrings(string message)
    {
        return ConnectionStringPattern().Replace(message, "$1[REDACTED]");
    }

    /// <summary>
    /// Redacts connection string value from a ConnectionConfig, returning a safe copy.
    /// </summary>
    public static ConnectionConfig RedactConnection(ConnectionConfig connection)
    {
        return new ConnectionConfig
        {
            Provider = connection.Provider,
            ConnectionString = "[REDACTED]"
        };
    }

    private static LogEventLevel ParseLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Debug
        };
    }

    private static Serilog.RollingInterval ParseRollingInterval(string interval)
    {
        return interval.ToLowerInvariant() switch
        {
            "infinite" => Serilog.RollingInterval.Infinite,
            "year" => Serilog.RollingInterval.Year,
            "month" => Serilog.RollingInterval.Month,
            "day" => Serilog.RollingInterval.Day,
            "hour" => Serilog.RollingInterval.Hour,
            "minute" => Serilog.RollingInterval.Minute,
            _ => Serilog.RollingInterval.Day
        };
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
        _serilogLogger?.Dispose();
        _loggerFactory = null;
        _serilogLogger = null;
    }
}
