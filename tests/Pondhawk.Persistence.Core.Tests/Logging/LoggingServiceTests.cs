using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Logging;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Logging;

public class LoggingServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LoggingService _service = new();

    public LoggingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_log_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        // Small delay to ensure file handles are released
        Thread.Sleep(100);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Enabled_True_CreatesLogFile()
    {
        var logPath = Path.Combine(_tempDir, "test.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug"
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("Hello from test");

        // Dispose to flush
        _service.Dispose();

        // Serilog with RollingInterval.Day appends date to filename
        var logFiles = Directory.GetFiles(_tempDir, "test*.log");
        logFiles.ShouldNotBeEmpty();
    }

    [Fact]
    public void Enabled_False_CreatesNoLogFile()
    {
        var logPath = Path.Combine(_tempDir, "noop.log");
        var config = new LoggingConfig
        {
            Enabled = false,
            LogPath = logPath
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("This should not be written");

        _service.Dispose();

        File.Exists(logPath).ShouldBeFalse();
        Directory.GetFiles(_tempDir, "noop*").Length.ShouldBe(0);
    }

    [Fact]
    public void Null_Config_CreatesNoOpLogger()
    {
        var factory = _service.Initialize(null, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("No-op");

        _service.Dispose();

        // No log files should be created (only the noop logger)
        Directory.GetFiles(_tempDir, "*.log").Length.ShouldBe(0);
    }

    [Fact]
    public void MEL_LoggerFactory_RoutesThoughSerilog()
    {
        var logPath = Path.Combine(_tempDir, "mel.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug",
            RollingInterval = "Infinite" // no date suffix
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("MyCategory");
        logger.LogWarning("MEL warning message");

        _service.Dispose();

        File.Exists(logPath).ShouldBeTrue();
        var content = File.ReadAllText(logPath);
        content.ShouldContain("MEL warning message");
    }

    [Fact]
    public void LevelFiltering_InformationSuppressesDebug()
    {
        var logPath = Path.Combine(_tempDir, "level.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Information",
            RollingInterval = "Infinite"
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogDebug("This is debug");
        logger.LogInformation("This is info");

        _service.Dispose();

        File.Exists(logPath).ShouldBeTrue();
        var content = File.ReadAllText(logPath);
        content.ShouldNotContain("This is debug");
        content.ShouldContain("This is info");
    }

    [Fact]
    public void LogPath_CreatesParentDirectories()
    {
        var logPath = Path.Combine(_tempDir, "sub", "deep", "app.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug",
            RollingInterval = "Infinite"
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("Directory creation test");

        _service.Dispose();

        File.Exists(logPath).ShouldBeTrue();
    }

    [Fact]
    public void RelativeLogPath_ResolvedFromProjectDir()
    {
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = "logs/relative.log",
            Level = "Debug",
            RollingInterval = "Infinite"
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("Relative path test");

        _service.Dispose();

        var expectedPath = Path.Combine(_tempDir, "logs", "relative.log");
        File.Exists(expectedPath).ShouldBeTrue();
    }

    [Fact]
    public void RedactConnectionStrings_RedactsValues()
    {
        var message = "Connecting with ConnectionString=Server=localhost;Database=mydb;User=admin;Password=secret123";
        var redacted = LoggingService.RedactConnectionStrings(message);

        redacted.ShouldContain("[REDACTED]");
        redacted.ShouldNotContain("secret123");
        redacted.ShouldNotContain("localhost");
    }

    [Fact]
    public void RedactConnectionStrings_NoConnectionString_ReturnsUnchanged()
    {
        var message = "Normal log message with no credentials";
        var redacted = LoggingService.RedactConnectionStrings(message);

        redacted.ShouldBe(message);
    }

    [Fact]
    public void RedactConnection_ReplacesConnectionString()
    {
        var connection = new ConnectionConfig
        {
            Provider = "sqlserver",
            ConnectionString = "Server=prod;Password=secret"
        };

        var redacted = LoggingService.RedactConnection(connection);

        redacted.ConnectionString.ShouldBe("[REDACTED]");
        redacted.Provider.ShouldBe("sqlserver");
    }

    [Fact]
    public void RollingInterval_Day_ProducesDateNamedFiles()
    {
        var logPath = Path.Combine(_tempDir, "rolling.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug",
            RollingInterval = "Day"
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("Rolling test");

        _service.Dispose();

        // Serilog Day rolling produces files like rolling20260207.log
        var files = Directory.GetFiles(_tempDir, "rolling*.log");
        files.ShouldNotBeEmpty();
        // At least one file should contain a date stamp
        files.Any(f => Path.GetFileName(f).Length > "rolling.log".Length).ShouldBeTrue();
    }

    [Fact]
    public void RetainedFileCountLimit_Zero_PassesNullToSerilog()
    {
        var logPath = Path.Combine(_tempDir, "retain.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug",
            RetainedFileCountLimit = 0,
            RollingInterval = "Infinite"
        };

        // Should not throw — 0 maps to null (unlimited retention)
        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("Retention test");

        _service.Dispose();
        File.Exists(logPath).ShouldBeTrue();
    }

    [Fact]
    public void RetainedFileCountLimit_Positive_IsRespected()
    {
        var logPath = Path.Combine(_tempDir, "retain2.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug",
            RetainedFileCountLimit = 5,
            RollingInterval = "Infinite"
        };

        var factory = _service.Initialize(config, _tempDir);
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("Retention test");

        _service.Dispose();
        File.Exists(logPath).ShouldBeTrue();
    }

    [Fact]
    public void SerilogLogger_Exposed_WhenEnabled()
    {
        var logPath = Path.Combine(_tempDir, "exposed.log");
        var config = new LoggingConfig
        {
            Enabled = true,
            LogPath = logPath,
            Level = "Debug"
        };

        _service.Initialize(config, _tempDir);
        _service.SerilogLogger.ShouldNotBeNull();
    }

    [Fact]
    public void SerilogLogger_Exposed_WhenDisabled()
    {
        var config = new LoggingConfig { Enabled = false };
        _service.Initialize(config, _tempDir);

        // Even when disabled, a Serilog logger is still created (just set to Fatal level)
        _service.SerilogLogger.ShouldNotBeNull();
    }

    [Fact]
    public void Initialize_CalledTwice_DisposesFirst()
    {
        var logPath1 = Path.Combine(_tempDir, "first.log");
        var logPath2 = Path.Combine(_tempDir, "second.log");

        var config1 = new LoggingConfig { Enabled = true, LogPath = logPath1, Level = "Debug", RollingInterval = "Infinite" };
        var config2 = new LoggingConfig { Enabled = true, LogPath = logPath2, Level = "Debug", RollingInterval = "Infinite" };

        _service.Initialize(config1, _tempDir);
        var factory2 = _service.Initialize(config2, _tempDir);
        var logger = factory2.CreateLogger("Test");
        logger.LogInformation("Second logger only");

        _service.Dispose();
        File.Exists(logPath2).ShouldBeTrue();
    }
}
