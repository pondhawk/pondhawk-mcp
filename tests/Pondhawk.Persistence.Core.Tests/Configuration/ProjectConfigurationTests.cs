using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Configuration;

public class ProjectConfigurationTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleConfigs", name);

    [Fact]
    public void Load_MinimalConfig_DeserializesCorrectly()
    {
        var config = ProjectConfigurationLoader.Load(FixturePath("minimal.json"));

        config.OutputDir.ShouldBe("src/Generated");
        config.Templates.ShouldContainKey("entity");
        config.Templates["entity"].Scope.ShouldBe("PerItem");
        config.Templates["entity"].Mode.ShouldBe("Always");
    }

    [Fact]
    public void Load_FullConfig_DeserializesAllSections()
    {
        var config = ProjectConfigurationLoader.Load(FixturePath("full.json"));

        config.ProjectName.ShouldBe("catalog");
        config.OutputDir.ShouldBe("src/Generated");

        config.Templates.Count.ShouldBe(3);
        config.Templates["registry"].Scope.ShouldBe("Single");
        config.Templates["entity"].AppliesTo.ShouldBe("Class");

        config.Values["Namespace"].ShouldBe("Catalog.Data");
        config.Values["Retries"].ShouldBe(3L);
        config.Values["Strict"].ShouldBe(true);

        config.Overrides.Count.ShouldBe(4);
        config.Overrides[0].Path.ShouldBe("*/CreatedAt");
        config.Overrides[0].Variant.ShouldBe("AuditTimestamp");
        config.Overrides[2].Metadata.ShouldNotBeNull();
        config.Overrides[2].Metadata!["Access"].ShouldBe("internal");
        config.Overrides[3].Ignore.ShouldBeTrue();

        config.Logging.Enabled.ShouldBeTrue();
        config.Logging.Level.ShouldBe("Debug");
        config.Logging.RollingInterval.ShouldBe("Day");
        config.Logging.RetainedFileCountLimit.ShouldBe(7);
    }

    [Fact]
    public void Values_PreserveJsonTypes()
    {
        // Values reach templates directly, so they must arrive as CLR primitives
        // rather than JsonElement.
        var config = ProjectConfigurationLoader.Deserialize("""
            { "Values": { "Text": "x", "Count": 42, "Ratio": 1.5, "On": true, "Off": null } }
            """);

        config.Values["Text"].ShouldBe("x");
        config.Values["Count"].ShouldBe(42L);
        config.Values["Ratio"].ShouldBe(1.5);
        config.Values["On"].ShouldBe(true);
        config.Values["Off"].ShouldBeNull();
    }

    [Fact]
    public void Values_AreCaseInsensitive()
    {
        var config = ProjectConfigurationLoader.Deserialize("""
            { "Values": { "Namespace": "MyApp" } }
            """);

        config.Values["namespace"].ShouldBe("MyApp");
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFoundException()
    {
        Should.Throw<FileNotFoundException>(() =>
            ProjectConfigurationLoader.Load("nonexistent.json"));
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() =>
            ProjectConfigurationLoader.Deserialize("{ invalid json }"));
    }

    [Fact]
    public void Deserialize_EmptyObject_ReturnsDefaults()
    {
        var config = ProjectConfigurationLoader.Deserialize("{}");

        config.OutputDir.ShouldBe("");
        config.Templates.ShouldBeEmpty();
        config.Values.ShouldBeEmpty();
        config.Overrides.ShouldBeEmpty();
        config.Logging.Enabled.ShouldBeFalse();
        config.Logging.LogPath.ShouldBe(".pondhawk/logs/pondhawk.log");
    }

    [Fact]
    public void Serialize_RoundTrips_Correctly()
    {
        var original = ProjectConfigurationLoader.Load(FixturePath("full.json"));
        var json = ProjectConfigurationLoader.Serialize(original);
        var roundTripped = ProjectConfigurationLoader.Deserialize(json);

        roundTripped.OutputDir.ShouldBe(original.OutputDir);
        roundTripped.Values.Count.ShouldBe(original.Values.Count);
        roundTripped.Templates.Count.ShouldBe(original.Templates.Count);
        roundTripped.Overrides.Count.ShouldBe(original.Overrides.Count);
    }

    [Fact]
    public void Deserialize_ProjectNameAndDescription_RoundTrips()
    {
        var json = """
        {
            "ProjectName": "connect-accounting",
            "Description": "Accounting database for Connect platform",
            "OutputDir": "out",
            "Templates": {}
        }
        """;
        var config = ProjectConfigurationLoader.Deserialize(json);

        config.ProjectName.ShouldBe("connect-accounting");
        config.Description.ShouldBe("Accounting database for Connect platform");

        var serialized = ProjectConfigurationLoader.Serialize(config);
        var roundTripped = ProjectConfigurationLoader.Deserialize(serialized);

        roundTripped.ProjectName.ShouldBe("connect-accounting");
        roundTripped.Description.ShouldBe("Accounting database for Connect platform");
    }

    [Fact]
    public void Deserialize_ProjectNameAbsent_IsNull()
    {
        var config = ProjectConfigurationLoader.Deserialize("{}");

        config.ProjectName.ShouldBeNull();
        config.Description.ShouldBeNull();
    }

    [Fact]
    public void Serialize_NullProjectName_OmitsFromJson()
    {
        var config = new ProjectConfiguration();
        var json = ProjectConfigurationLoader.Serialize(config);

        json.ShouldNotContain("ProjectName");
        json.ShouldNotContain("Description");
    }

    [Fact]
    public void Deserialize_LoggingSectionAbsent_UsesDefaults()
    {
        var json = """
        {
            "OutputDir": "out",
            "Templates": {}
        }
        """;
        var config = ProjectConfigurationLoader.Deserialize(json);

        config.Logging.Enabled.ShouldBeFalse();
        config.Logging.Level.ShouldBe("Debug");
        config.Logging.RollingInterval.ShouldBe("Day");
        config.Logging.RetainedFileCountLimit.ShouldBe(7);
    }
}
