using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Migrations;
using Pondhawk.Persistence.Core.Models;
using Shouldly;
using Attribute = Pondhawk.Persistence.Core.Models.Attribute;

namespace Pondhawk.Persistence.Core.Tests.Migrations;

public class MigrationFileManagerTests : IDisposable
{
    private readonly string _tempDir;

    public MigrationFileManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_mfm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetNextVersion_EmptyDir_Returns1()
    {
        var mgr = new MigrationFileManager(Path.Combine(_tempDir, "migrations"));
        mgr.GetNextVersion().ShouldBe(1);
    }

    [Fact]
    public void GetNextVersion_NonExistentDir_Returns1()
    {
        var mgr = new MigrationFileManager(Path.Combine(_tempDir, "nonexistent"));
        mgr.GetNextVersion().ShouldBe(1);
    }

    [Fact]
    public void GetNextVersion_WithExistingMigrations()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.sql"), "-- sql");
        File.WriteAllText(Path.Combine(migrationsDir, "V002__add_users.sql"), "-- sql");

        var mgr = new MigrationFileManager(migrationsDir);
        mgr.GetNextVersion().ShouldBe(3);
    }

    [Fact]
    public void GetLatestSnapshotPath_NoSnapshots_ReturnsNull()
    {
        var mgr = new MigrationFileManager(Path.Combine(_tempDir, "nonexistent"));
        mgr.GetLatestSnapshotPath().ShouldBeNull();
    }

    [Fact]
    public void GetLatestSnapshotPath_ReturnsHighestVersion()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.json"), "{}");
        File.WriteAllText(Path.Combine(migrationsDir, "V002__add_users.json"), "{}");

        var mgr = new MigrationFileManager(migrationsDir);
        var path = mgr.GetLatestSnapshotPath();
        path.ShouldNotBeNull();
        Path.GetFileName(path).ShouldBe("V002__add_users.json");
    }

    [Fact]
    public void LoadBaselineModels_NoSnapshot_ReturnsEmpty()
    {
        var mgr = new MigrationFileManager(Path.Combine(_tempDir, "nonexistent"));
        mgr.LoadBaselineModels().ShouldBeEmpty();
    }

    [Fact]
    public void LoadBaselineModels_WithSnapshot_ReturnsModels()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);

        var models = new List<Model>
        {
            new()
            {
                Name = "Users", Schema = "dbo",
                Attributes = [new Attribute { Name = "Id", DataType = "int" }]
            }
        };
        var schemaFile = SchemaFileMapper.ToSchemaFile(models, "TestDb", "sqlserver", "design");
        var json = SchemaFileMapper.Serialize(schemaFile);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.json"), json);

        var mgr = new MigrationFileManager(migrationsDir);
        var loaded = mgr.LoadBaselineModels();
        loaded.Count.ShouldBe(1);
        loaded[0].Name.ShouldBe("Users");
    }

    [Fact]
    public void ValidateHistory_ValidPairing()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.sql"), "-- sql");
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.json"), "{}");

        var mgr = new MigrationFileManager(migrationsDir);
        mgr.ValidateHistory().ShouldBeEmpty();
    }

    [Fact]
    public void ValidateHistory_MissingSqlFile()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.json"), "{}");

        var mgr = new MigrationFileManager(migrationsDir);
        var errors = mgr.ValidateHistory();
        errors.Count.ShouldBe(1);
        errors[0].ShouldContain(".json snapshot but no matching .sql");
    }

    [Fact]
    public void ValidateHistory_MissingJsonFile()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);
        File.WriteAllText(Path.Combine(migrationsDir, "V001__initial.sql"), "-- sql");

        var mgr = new MigrationFileManager(migrationsDir);
        var errors = mgr.ValidateHistory();
        errors.Count.ShouldBe(1);
        errors[0].ShouldContain(".sql file but no matching .json");
    }

    [Fact]
    public void WriteMigration_CreatesFiles()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        var mgr = new MigrationFileManager(migrationsDir);

        var (sqlPath, snapshotPath) = mgr.WriteMigration(1, "initial schema", "CREATE TABLE Users;", "{\"test\":true}");

        File.Exists(sqlPath).ShouldBeTrue();
        File.Exists(snapshotPath).ShouldBeTrue();
        File.ReadAllText(sqlPath).ShouldBe("CREATE TABLE Users;");
        File.ReadAllText(snapshotPath).ShouldBe("{\"test\":true}");
    }

    [Fact]
    public void WriteMigration_CreatesDirectory()
    {
        var migrationsDir = Path.Combine(_tempDir, "new_dir", "migrations");
        var mgr = new MigrationFileManager(migrationsDir);

        mgr.WriteMigration(1, "initial", "-- sql", "{}");

        Directory.Exists(migrationsDir).ShouldBeTrue();
    }

    [Fact]
    public void WriteMigration_FileNaming()
    {
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        var mgr = new MigrationFileManager(migrationsDir);

        var (sqlPath, snapshotPath) = mgr.WriteMigration(1, "Add Users Table", "-- sql", "{}");

        Path.GetFileName(sqlPath).ShouldBe("V001__add_users_table.sql");
        Path.GetFileName(snapshotPath).ShouldBe("V001__add_users_table.json");
    }

    [Theory]
    [InlineData("add users table", "add_users_table")]
    [InlineData("Add-Users-Table", "add_users_table")]
    [InlineData("  spaces  and  stuff  ", "spaces_and_stuff")]
    [InlineData("Special!@#$%Characters", "special_characters")]
    public void Slugify(string input, string expected)
    {
        MigrationFileManager.Slugify(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, "001")]
    [InlineData(42, "042")]
    [InlineData(999, "999")]
    [InlineData(1000, "1000")]
    [InlineData(1234, "1234")]
    public void FormatVersion(int version, string expected)
    {
        MigrationFileManager.FormatVersion(version).ShouldBe(expected);
    }
}
