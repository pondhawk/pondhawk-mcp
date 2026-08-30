using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests;

public class ServerContextTests : IDisposable
{
    private readonly string _tempDir;

    public ServerContextTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_ctx_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static ProjectConfiguration Config(Action<ProjectConfiguration>? customize = null)
    {
        var config = new ProjectConfiguration { OutputDir = "src/Generated" };
        customize?.Invoke(config);
        return config;
    }

    private void WriteConfig(ProjectConfiguration config) =>
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);

    // --- Paths ---------------------------------------------------------------

    [Fact]
    public void ProjectDir_RelativePath_IsMadeAbsolute()
    {
        using var context = new ServerContext(".");

        Path.IsPathRooted(context.ProjectDir).ShouldBeTrue();
        context.ProjectDir.ShouldBe(Path.GetFullPath("."));
    }

    [Fact]
    public void ConfigPath_And_ModelPath_AreRootedInProjectDir()
    {
        using var context = new ServerContext(_tempDir);

        context.ConfigPath.ShouldBe(Path.Combine(context.ProjectDir, "pondhawk.project.json"));
        context.ModelPath.ShouldBe(Path.Combine(context.ProjectDir, "model.json"));
    }

    // --- InitializeLogging ---------------------------------------------------

    [Fact]
    public void InitializeLogging_NoConfigFile_StillProducesALoggerFactory()
    {
        using var context = new ServerContext(_tempDir);

        context.LoggerFactory.ShouldBeNull();
        context.InitializeLogging();

        context.LoggerFactory.ShouldNotBeNull();
    }

    [Fact]
    public void InitializeLogging_LoggingEnabled_CreatesTheLogDirectory()
    {
        WriteConfig(Config(c =>
        {
            c.Logging.Enabled = true;
            c.Logging.LogPath = "logs/pondhawk.log";
        }));

        using var context = new ServerContext(_tempDir);
        context.InitializeLogging();

        Directory.Exists(Path.Combine(_tempDir, "logs")).ShouldBeTrue();
    }

    [Fact]
    public void InitializeLogging_MalformedConfig_DoesNotThrowAndStillLogs()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pondhawk.project.json"), "{ not json");

        using var context = new ServerContext(_tempDir);

        Should.NotThrow(() => context.InitializeLogging());
        context.LoggerFactory.ShouldNotBeNull();
    }

    [Fact]
    public void InitializeLogging_MalformedConfig_WarnsOnStderrAndNeverOnStdout()
    {
        // The server speaks MCP over stdout, so a stray diagnostic there corrupts the
        // protocol stream. The warning must go to stderr and nowhere else.
        File.WriteAllText(Path.Combine(_tempDir, "pondhawk.project.json"), "{ not json");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            using var context = new ServerContext(_tempDir);
            context.InitializeLogging();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        stderr.ToString().ShouldContain("failed to read config for logging setup");
        stdout.ToString().ShouldBeEmpty();
    }

    // --- EnsureConfig --------------------------------------------------------

    [Fact]
    public void EnsureConfig_ReturnsTheConfigOnDisk()
    {
        WriteConfig(Config(c => c.ProjectName = "demo"));

        using var context = new ServerContext(_tempDir);

        context.EnsureConfig().ProjectName.ShouldBe("demo");
    }

    [Fact]
    public void EnsureConfig_MissingConfig_Throws()
    {
        using var context = new ServerContext(_tempDir);

        Should.Throw<FileNotFoundException>(() => context.EnsureConfig());
    }

    [Fact]
    public void EnsureConfig_FirstCall_InitializesLogging()
    {
        WriteConfig(Config());

        using var context = new ServerContext(_tempDir);
        context.EnsureConfig();

        context.LoggerFactory.ShouldNotBeNull();
    }

    [Fact]
    public void EnsureConfig_UnchangedLoggingConfig_ReusesTheSameLoggerFactory()
    {
        WriteConfig(Config());

        using var context = new ServerContext(_tempDir);
        context.EnsureConfig();
        var first = context.LoggerFactory;

        context.EnsureConfig();

        context.LoggerFactory.ShouldBeSameAs(first);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Level")]
    [InlineData("LogPath")]
    public void EnsureConfig_ChangedLoggingConfig_RebuildsTheLoggerFactory(string changedField)
    {
        WriteConfig(Config(c =>
        {
            c.Logging.Enabled = true;
            c.Logging.Level = "Debug";
            c.Logging.LogPath = "logs/a.log";
        }));

        using var context = new ServerContext(_tempDir);
        context.EnsureConfig();
        var first = context.LoggerFactory;

        WriteConfig(Config(c =>
        {
            c.Logging.Enabled = changedField != "Enabled";
            c.Logging.Level = changedField == "Level" ? "Warning" : "Debug";
            c.Logging.LogPath = changedField == "LogPath" ? "logs/b.log" : "logs/a.log";
        }));
        TouchConfig();

        context.EnsureConfig();

        context.LoggerFactory.ShouldNotBeSameAs(first);
    }

    /// <summary>
    /// The cache keys on last-write time, so a rewrite within the same filesystem
    /// timestamp tick would otherwise be served from cache and the test would pass
    /// for the wrong reason.
    /// </summary>
    private void TouchConfig() =>
        File.SetLastWriteTimeUtc(
            Path.Combine(_tempDir, "pondhawk.project.json"),
            DateTime.UtcNow.AddSeconds(1));

    // --- ResolveConfig -------------------------------------------------------

    [Fact]
    public void ResolveConfig_SubstitutesValuesFromTheEnvFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".env"), "LICENCE_KEY=abc-123\n");

        using var context = new ServerContext(_tempDir);
        var config = Config(c => c.Values["licence"] = "${LICENCE_KEY}");

        context.ResolveConfig(config).Values["licence"].ShouldBe("abc-123");
    }

    [Fact]
    public void ResolveConfig_ReturnsTheSameInstanceItWasGiven()
    {
        using var context = new ServerContext(_tempDir);
        var config = Config();

        context.ResolveConfig(config).ShouldBeSameAs(config);
    }

    [Fact]
    public void ResolveConfig_NoEnvFile_LeavesPlainValuesAlone()
    {
        using var context = new ServerContext(_tempDir);
        var config = Config(c => c.Values["name"] = "literal");

        context.ResolveConfig(config).Values["name"].ShouldBe("literal");
    }

    [Fact]
    public void ResolveConfig_UnresolvableReference_Throws()
    {
        using var context = new ServerContext(_tempDir);
        var config = Config(c => c.Values["missing"] = "${PONDHAWK_NOT_SET_ANYWHERE}");

        Should.Throw<EnvironmentVariableNotFoundException>(() => context.ResolveConfig(config));
    }

    [Fact]
    public void ResolveConfig_NonStringValues_PassThroughUntouched()
    {
        using var context = new ServerContext(_tempDir);
        var config = Config(c =>
        {
            c.Values["count"] = 42;
            c.Values["flag"] = true;
            c.Values["nothing"] = null;
        });

        var resolved = context.ResolveConfig(config);

        resolved.Values["count"].ShouldBe(42);
        resolved.Values["flag"].ShouldBe(true);
        resolved.Values["nothing"].ShouldBeNull();
    }

    // --- StartToolCall -------------------------------------------------------

    [Fact]
    public void StartToolCall_BeforeLoggingIsInitialized_FallsBackToANullLogger()
    {
        using var context = new ServerContext(_tempDir);

        var (logger, stopwatch) = context.StartToolCall("generate");

        logger.ShouldNotBeNull();
        stopwatch.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void StartToolCall_AfterLoggingIsInitialized_ReturnsARunningStopwatch()
    {
        using var context = new ServerContext(_tempDir);
        context.InitializeLogging();

        var (logger, stopwatch) = context.StartToolCall("generate", "artifact=entity");

        logger.ShouldNotBeNull();
        stopwatch.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void StartToolCall_LoggingEnabled_WritesTheCallToTheLogFile()
    {
        WriteConfig(Config(c =>
        {
            c.Logging.Enabled = true;
            c.Logging.Level = "Information";
            c.Logging.LogPath = "logs/pondhawk.log";
        }));

        using (var context = new ServerContext(_tempDir))
        {
            context.InitializeLogging();
            context.StartToolCall("generate", "artifact=entity");
        }

        // Dispose flushes and releases the file, so read only after the using block.
        var logFile = Directory.GetFiles(Path.Combine(_tempDir, "logs")).ShouldHaveSingleItem();
        var contents = File.ReadAllText(logFile);
        contents.ShouldContain("generate");
        contents.ShouldContain("artifact=entity");
    }

    // --- Dispose -------------------------------------------------------------

    [Fact]
    public void Dispose_IsSafeToCallMoreThanOnce()
    {
        var context = new ServerContext(_tempDir);
        context.InitializeLogging();

        Should.NotThrow(() =>
        {
            context.Dispose();
            context.Dispose();
        });
    }

    [Fact]
    public void Dispose_WithoutLoggingEverInitialized_DoesNotThrow()
    {
        var context = new ServerContext(_tempDir);

        Should.NotThrow(context.Dispose);
    }
}
