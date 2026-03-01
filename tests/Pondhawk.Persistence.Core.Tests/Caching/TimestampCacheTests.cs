using Pondhawk.Persistence.Core.Caching;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;
using Pondhawk.Persistence.Core.Rendering;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Caching;

public class TimestampCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateEngine _engine = new();
    private readonly TimestampCache _cache;

    public TimestampCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cache = new TimestampCache(_engine);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string SchemaPath => Path.Combine(_tempDir, "schema.json");

    private string WriteConfigFile(string? json = null)
    {
        json ??= """
            {
                "Connection": {},
                "OutputDir": "generated",
                "Templates": {},
                "Defaults": { "Schema": "dbo" }
            }
            """;
        var path = Path.Combine(_tempDir, "persistence.project.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string WriteTemplateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void GetConfiguration_FirstAccess_LoadsFromDisk()
    {
        var configPath = WriteConfigFile();

        var config = _cache.GetConfiguration(configPath);

        config.ShouldNotBeNull();
        config.OutputDir.ShouldBe("generated");
    }

    [Fact]
    public void GetConfiguration_CacheHit_WhenFileUnchanged()
    {
        var configPath = WriteConfigFile();

        var first = _cache.GetConfiguration(configPath);
        var second = _cache.GetConfiguration(configPath);

        // Should return the same instance (cached)
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void GetConfiguration_CacheMiss_WhenFileTimestampChanges()
    {
        var configPath = WriteConfigFile();
        var first = _cache.GetConfiguration(configPath);

        // Modify the file to change its timestamp
        Thread.Sleep(50); // Ensure different timestamp
        File.WriteAllText(configPath, """
            {
                "Connection": {},
                "OutputDir": "updated",
                "Templates": {},
                "Defaults": { "Schema": "dbo" }
            }
            """);

        var second = _cache.GetConfiguration(configPath);

        ReferenceEquals(first, second).ShouldBeFalse();
        second.OutputDir.ShouldBe("updated");
    }

    [Fact]
    public void GetConfiguration_ConfigChange_InvalidatesConfigAndTemplates_NotSchema()
    {
        var configPath = WriteConfigFile();
        var templatePath = WriteTemplateFile("test.liquid", "{{ entity.Name }}");

        // Load config and template into cache
        _cache.GetConfiguration(configPath);
        _cache.GetTemplate(templatePath);

        // Write a schema file
        _cache.SetSchema([new Model { Name = "Products" }], SchemaPath, "TestDb", "sqlite");

        // Verify they are cached
        _cache.HasSchema(SchemaPath).ShouldBeTrue();
        _cache.IsTemplateStale(templatePath).ShouldBeFalse();

        // Modify config file
        Thread.Sleep(50);
        File.WriteAllText(configPath, """
            {
                "Connection": {},
                "OutputDir": "changed",
                "Templates": {},
                "Defaults": { "Schema": "dbo" }
            }
            """);

        // Reload config — should invalidate config+templates but NOT schema
        _cache.GetConfiguration(configPath);

        // Schema should still exist on disk
        _cache.HasSchema(SchemaPath).ShouldBeTrue();
        // Template cache should be invalidated
        _cache.IsTemplateStale(templatePath).ShouldBeTrue();
    }

    [Fact]
    public void GetTemplate_FirstAccess_CompilesFromDisk()
    {
        var templatePath = WriteTemplateFile("test.liquid", "Hello {{ name }}");

        var template = _cache.GetTemplate(templatePath);

        template.ShouldNotBeNull();
    }

    [Fact]
    public void GetTemplate_CacheHit_WhenFileUnchanged()
    {
        var templatePath = WriteTemplateFile("test.liquid", "Hello {{ name }}");

        var first = _cache.GetTemplate(templatePath);
        var second = _cache.GetTemplate(templatePath);

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void GetTemplate_CacheMiss_WhenFileTimestampChanges()
    {
        var templatePath = WriteTemplateFile("test.liquid", "Hello {{ name }}");
        var first = _cache.GetTemplate(templatePath);

        Thread.Sleep(50);
        File.WriteAllText(templatePath, "Goodbye {{ name }}");

        var second = _cache.GetTemplate(templatePath);

        ReferenceEquals(first, second).ShouldBeFalse();
    }

    [Fact]
    public void GetTemplate_TemplateChange_InvalidatesOnlyThatTemplate()
    {
        var template1 = WriteTemplateFile("a.liquid", "A");
        var template2 = WriteTemplateFile("b.liquid", "B");

        var first1 = _cache.GetTemplate(template1);
        var first2 = _cache.GetTemplate(template2);

        // Modify only template1
        Thread.Sleep(50);
        File.WriteAllText(template1, "A updated");

        // template1 should be stale, template2 should not
        _cache.IsTemplateStale(template1).ShouldBeTrue();
        _cache.IsTemplateStale(template2).ShouldBeFalse();

        // Reloading template1 should not affect template2
        _cache.GetTemplate(template1);
        var second2 = _cache.GetTemplate(template2);
        ReferenceEquals(first2, second2).ShouldBeTrue();
    }

    [Fact]
    public void GetTemplate_InvalidTemplate_ThrowsException()
    {
        var templatePath = WriteTemplateFile("bad.liquid", "{% if %}");

        Should.Throw<InvalidOperationException>(() => _cache.GetTemplate(templatePath));
    }

    [Fact]
    public void GetSchema_ReturnsNull_WhenNoSchemaFile()
    {
        _cache.GetSchema(SchemaPath).ShouldBeNull();
    }

    [Fact]
    public void GetSchema_ReturnsCached_AfterSet()
    {
        var models = new List<Model> { new() { Name = "Products", Schema = "main" } };
        _cache.SetSchema(models, SchemaPath, "TestDb", "sqlite");

        var cached = _cache.GetSchema(SchemaPath);
        cached.ShouldNotBeNull();
        cached.Count.ShouldBe(1);
        cached[0].Name.ShouldBe("Products");
    }

    [Fact]
    public void SetSchema_WritesSchemaJsonToDisk()
    {
        var models = new List<Model> { new() { Name = "Products", Schema = "main" } };
        _cache.SetSchema(models, SchemaPath, "TestDb", "sqlite");

        File.Exists(SchemaPath).ShouldBeTrue();
        var json = File.ReadAllText(SchemaPath);
        json.ShouldContain("Products");
        json.ShouldContain("TestDb");
        json.ShouldContain("sqlite");
    }

    [Fact]
    public void GetSchemaFile_ReturnsMetadata()
    {
        var models = new List<Model> { new() { Name = "Products", Schema = "main" } };
        _cache.SetSchema(models, SchemaPath, "TestDb", "sqlite");

        var schemaFile = _cache.GetSchemaFile(SchemaPath);
        schemaFile.ShouldNotBeNull();
        schemaFile.Database.ShouldBe("TestDb");
        schemaFile.Provider.ShouldBe("sqlite");
    }

    [Fact]
    public void UpdateConfigTimestampAfterWriteBack_DoesNotInvalidateSchema()
    {
        var configPath = WriteConfigFile();

        // Prime the caches
        _cache.GetConfiguration(configPath);
        _cache.SetSchema([new Model { Name = "Products", Schema = "main" }], SchemaPath, "TestDb", "sqlite");

        // Simulate TypeMappings write-back modifying the config file
        Thread.Sleep(50);
        File.WriteAllText(configPath, """
            {
                "Connection": {},
                "OutputDir": "generated",
                "Templates": {},
                "Defaults": { "Schema": "dbo" },
                "TypeMappings": [{ "DbType": "int", "ClrType": "int" }]
            }
            """);

        // Update config timestamp without invalidating schema
        _cache.UpdateConfigTimestampAfterWriteBack(configPath);

        // Schema should still be cached
        _cache.HasSchema(SchemaPath).ShouldBeTrue();
        var cached = _cache.GetSchema(SchemaPath);
        cached.ShouldNotBeNull();
        cached[0].Name.ShouldBe("Products");

        // Config should reflect the new content
        _cache.GetConfiguration(configPath).TypeMappings.Count.ShouldBe(1);
    }

    [Fact]
    public void IsConfigStale_ReturnsTrueOnFirstAccess()
    {
        var configPath = WriteConfigFile();
        _cache.IsConfigStale(configPath).ShouldBeTrue();
    }

    [Fact]
    public void IsConfigStale_ReturnsFalseAfterLoad()
    {
        var configPath = WriteConfigFile();
        _cache.GetConfiguration(configPath);
        _cache.IsConfigStale(configPath).ShouldBeFalse();
    }

    [Fact]
    public void IsTemplateStale_ReturnsTrueForUnknownTemplate()
    {
        _cache.IsTemplateStale("nonexistent.liquid").ShouldBeTrue();
    }
}
