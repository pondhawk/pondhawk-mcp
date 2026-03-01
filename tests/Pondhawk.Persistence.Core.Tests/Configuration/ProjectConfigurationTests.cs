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

        config.Connection.Provider.ShouldBe("sqlite");
        config.Connection.ConnectionString.ShouldBe("Data Source=test.db");
        config.OutputDir.ShouldBe("src/Data");
        config.Templates.ShouldContainKey("entity");
        config.Templates["entity"].Scope.ShouldBe("PerModel");
        config.Templates["entity"].Mode.ShouldBe("Always");
    }

    [Fact]
    public void Load_FullConfig_DeserializesAllSections()
    {
        var config = ProjectConfigurationLoader.Load(FixturePath("full.json"));

        config.Connection.Provider.ShouldBe("sqlserver");
        config.Connection.ConnectionString.ShouldContain("Inventory");

        config.DataTypes.ShouldContainKey("Uid");
        config.DataTypes["Uid"].ClrType.ShouldBe("string");
        config.DataTypes["Uid"].MaxLength.ShouldBe(28);
        config.DataTypes["Uid"].DefaultValue.ShouldBe("Ulid.NewUlid()");

        config.DataTypes.ShouldContainKey("Money");
        config.DataTypes["Money"].ClrType.ShouldBe("decimal");

        config.TypeMappings.Count.ShouldBe(3);
        config.TypeMappings[0].DbType.ShouldBe("char(28)");
        config.TypeMappings[0].DataType.ShouldBe("Uid");
        config.TypeMappings[2].ClrType.ShouldBe("byte");

        config.Templates.Count.ShouldBe(3);
        config.Templates["dbcontext"].Scope.ShouldBe("SingleFile");

        config.Defaults.Namespace.ShouldBe("MyApp.Data");
        config.Defaults.ContextName.ShouldBe("Inventory");
        config.Defaults.Schema.ShouldBe("dbo");
        config.Defaults.IncludeViews.ShouldBeFalse();
        config.Defaults.Include.ShouldNotBeNull();
        config.Defaults.Include!.Count.ShouldBe(3);
        config.Defaults.Exclude.ShouldNotBeNull();
        config.Defaults.Exclude!.Count.ShouldBe(2);

        config.Relationships.Count.ShouldBe(1);
        config.Relationships[0].DependentTable.ShouldBe("Products");
        config.Relationships[0].PrincipalTable.ShouldBe("Categories");
        config.Relationships[0].OnDelete.ShouldBe("NoAction");

        config.Overrides.Count.ShouldBe(3);
        config.Overrides[0].Class.ShouldBe("*");
        config.Overrides[0].Property.ShouldBe("Id");
        config.Overrides[0].DataType.ShouldBe("Uid");

        config.Logging.Enabled.ShouldBeFalse();
        config.Logging.Level.ShouldBe("Debug");
        config.Logging.RollingInterval.ShouldBe("Day");
        config.Logging.RetainedFileCountLimit.ShouldBe(7);
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

        config.Connection.ShouldNotBeNull();
        config.Connection.Provider.ShouldBe("");
        config.Connection.ConnectionString.ShouldBe("");
        config.OutputDir.ShouldBe("");
        config.Templates.ShouldBeEmpty();
        config.Defaults.ShouldNotBeNull();
        config.Defaults.Schema.ShouldBe("dbo");
        config.Defaults.IncludeViews.ShouldBeFalse();
        config.Logging.Enabled.ShouldBeFalse();
        config.Logging.LogPath.ShouldBe(".pondhawk/logs/pondhawk.log");
    }

    [Fact]
    public void Serialize_RoundTrips_Correctly()
    {
        var original = ProjectConfigurationLoader.Load(FixturePath("full.json"));
        var json = ProjectConfigurationLoader.Serialize(original);
        var roundTripped = ProjectConfigurationLoader.Deserialize(json);

        roundTripped.Connection.Provider.ShouldBe(original.Connection.Provider);
        roundTripped.DataTypes.Count.ShouldBe(original.DataTypes.Count);
        roundTripped.TypeMappings.Count.ShouldBe(original.TypeMappings.Count);
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
            "Connection": { "Provider": "sqlite", "ConnectionString": "test" },
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
            "Connection": { "Provider": "sqlite", "ConnectionString": "test" },
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
