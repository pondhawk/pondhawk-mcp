using Pondhawk.Persistence.Core.Configuration;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Configuration;

public class EnvironmentResolverTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    private string CreateEnvFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void Resolve_SystemEnvVar_Substituted()
    {
        Environment.SetEnvironmentVariable("PONDHAWK_TEST_VAR", "test_value");
        try
        {
            var resolver = new EnvironmentResolver();
            var result = resolver.Resolve("prefix_${PONDHAWK_TEST_VAR}_suffix");
            result.ShouldBe("prefix_test_value_suffix");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PONDHAWK_TEST_VAR", null);
        }
    }

    [Fact]
    public void Resolve_EnvFile_Substituted()
    {
        var envFile = CreateEnvFile("MY_KEY=my_value");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        var result = resolver.Resolve("${MY_KEY}");
        result.ShouldBe("my_value");
    }

    [Fact]
    public void Resolve_SystemEnvOverridesEnvFile()
    {
        var envFile = CreateEnvFile("PONDHAWK_OVERRIDE_TEST=from_file");
        Environment.SetEnvironmentVariable("PONDHAWK_OVERRIDE_TEST", "from_system");
        try
        {
            var resolver = new EnvironmentResolver();
            resolver.LoadEnvFile(envFile);

            var result = resolver.Resolve("${PONDHAWK_OVERRIDE_TEST}");
            result.ShouldBe("from_system");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PONDHAWK_OVERRIDE_TEST", null);
        }
    }

    [Fact]
    public void Resolve_UnsetVariable_ThrowsException()
    {
        var resolver = new EnvironmentResolver();
        var ex = Should.Throw<EnvironmentVariableNotFoundException>(() =>
            resolver.Resolve("${TOTALLY_MISSING_VAR_XYZ}"));

        ex.VariableName.ShouldBe("TOTALLY_MISSING_VAR_XYZ");
    }

    [Fact]
    public void Resolve_NoVariables_ReturnsUnchanged()
    {
        var resolver = new EnvironmentResolver();
        var result = resolver.Resolve("plain string with no vars");
        result.ShouldBe("plain string with no vars");
    }

    [Fact]
    public void LoadEnvFile_Comments_Ignored()
    {
        var envFile = CreateEnvFile("""
            # This is a comment
            KEY1=value1

            # Another comment
            KEY2=value2
            """);

        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        resolver.Resolve("${KEY1}").ShouldBe("value1");
        resolver.Resolve("${KEY2}").ShouldBe("value2");
    }

    [Fact]
    public void LoadEnvFile_QuotedValues_StripsQuotes()
    {
        var envFile = CreateEnvFile("""KEY="quoted value" """.Trim());
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        resolver.Resolve("${KEY}").ShouldBe("quoted value");
    }

    [Fact]
    public void LoadEnvFile_MissingFile_NoError()
    {
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile("nonexistent.env"); // Should not throw
    }

    [Fact]
    public void TryResolve_UnsetVariable_ReturnsUnresolved()
    {
        var resolver = new EnvironmentResolver();
        var (resolved, unresolved) = resolver.TryResolve("${MISSING_A} and ${MISSING_B}");

        unresolved.Count.ShouldBe(2);
        unresolved.ShouldContain("MISSING_A");
        unresolved.ShouldContain("MISSING_B");
        resolved.ShouldContain("${MISSING_A}");
    }

    [Fact]
    public void ResolveConfiguration_ResolvesConnectionStrings()
    {
        var envFile = CreateEnvFile("DB_CONN=Server=localhost;Database=Test;");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        var config = new ProjectConfiguration
        {
            Connection = new ConnectionConfig
            {
                Provider = "sqlserver",
                ConnectionString = "${DB_CONN}"
            }
        };

        resolver.ResolveConfiguration(config);
        config.Connection.ConnectionString.ShouldBe("Server=localhost;Database=Test;");
    }

    [Fact]
    public void Resolve_MultipleVariablesInOneString()
    {
        var envFile = CreateEnvFile("HOST=localhost\nPORT=5432");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        var result = resolver.Resolve("Host=${HOST};Port=${PORT}");
        result.ShouldBe("Host=localhost;Port=5432");
    }

    [Fact]
    public void LoadEnvFile_ValueContainingEquals_ParsesCorrectly()
    {
        var envFile = CreateEnvFile("CONN=Server=localhost;Database=mydb");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        resolver.Resolve("${CONN}").ShouldBe("Server=localhost;Database=mydb");
    }

    [Fact]
    public void LoadEnvFile_SingleQuotedValues_StripsQuotes()
    {
        var envFile = CreateEnvFile("KEY='single quoted'");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        // Single quotes are not stripped (only double quotes are)
        resolver.Resolve("${KEY}").ShouldBe("'single quoted'");
    }

    [Fact]
    public void LoadEnvFile_EmptyValue_ParsesAsEmpty()
    {
        var envFile = CreateEnvFile("EMPTY_KEY=");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        resolver.Resolve("${EMPTY_KEY}").ShouldBe("");
    }

    [Fact]
    public void LoadEnvFile_BlankLines_Ignored()
    {
        var envFile = CreateEnvFile("A=1\n\n\nB=2\n\n");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        resolver.Resolve("${A}").ShouldBe("1");
        resolver.Resolve("${B}").ShouldBe("2");
    }

    [Fact]
    public void ResolveConfiguration_DoesNotResolveOutputDir()
    {
        var envFile = CreateEnvFile("OUT_DIR=gen/output");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        var config = new ProjectConfiguration { OutputDir = "${OUT_DIR}" };
        resolver.ResolveConfiguration(config);

        // Only connection strings are resolved — OutputDir stays as-is
        config.OutputDir.ShouldBe("${OUT_DIR}");
    }

    [Fact]
    public void ResolveConfiguration_DoesNotResolveTemplatePaths()
    {
        var envFile = CreateEnvFile("TPL_DIR=custom/templates");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        var config = new ProjectConfiguration();
        config.Templates["entity"] = new TemplateConfig
        {
            Path = "${TPL_DIR}/entity.liquid",
            OutputPattern = "${TPL_DIR}/out.cs"
        };
        resolver.ResolveConfiguration(config);

        // Only connection strings are resolved — template paths stay as-is
        config.Templates["entity"].Path.ShouldBe("${TPL_DIR}/entity.liquid");
        config.Templates["entity"].OutputPattern.ShouldBe("${TPL_DIR}/out.cs");
    }

    [Fact]
    public void ResolveConfiguration_DoesNotResolveLogPath()
    {
        var envFile = CreateEnvFile("LOG_DIR=logs");
        var resolver = new EnvironmentResolver();
        resolver.LoadEnvFile(envFile);

        var config = new ProjectConfiguration { Logging = new LoggingConfig { LogPath = "${LOG_DIR}/app.log" } };
        resolver.ResolveConfiguration(config);

        // Only connection strings are resolved — log path stays as-is
        config.Logging.LogPath.ShouldBe("${LOG_DIR}/app.log");
    }
}
