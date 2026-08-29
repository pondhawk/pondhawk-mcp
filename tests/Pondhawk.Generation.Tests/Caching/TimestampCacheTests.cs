using Pondhawk.Generation.Caching;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;
using Shouldly;

namespace Pondhawk.Generation.Tests.Caching;

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

    private string ModelPath => Path.Combine(_tempDir, "model.json");

    private string WriteConfigFile(string? json = null)
    {
        json ??= """
            {
                "OutputDir": "generated",
                "Templates": {}
            }
            """;
        var path = Path.Combine(_tempDir, "pondhawk.project.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static ModelFile SampleModel() => new()
    {
        Name = "Sample",
        Nodes =
        [
            new Node
            {
                Name = "Products", Kind = "Class",
                Children = [new Node { Name = "Id", Kind = "Property" }]
            }
        ]
    };

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
                "OutputDir": "updated",
                "Templates": {}
            }
            """);

        var second = _cache.GetConfiguration(configPath);

        ReferenceEquals(first, second).ShouldBeFalse();
        second.OutputDir.ShouldBe("updated");
    }

    [Fact]
    public void GetConfiguration_ConfigChange_InvalidatesAllCaches()
    {
        var configPath = WriteConfigFile();
        var templatePath = WriteTemplateFile("test.liquid", "{{ entity.Name }}");

        // Load config and template into cache
        _cache.GetConfiguration(configPath);
        _cache.GetTemplate(templatePath);

        // Write a schema file and capture the cached instance
        _cache.SetModel(SampleModel(), ModelPath);
        var modelBefore = _cache.GetModel(ModelPath);

        // Verify they are cached
        _cache.HasModel(ModelPath).ShouldBeTrue();
        _cache.IsTemplateStale(templatePath).ShouldBeFalse();

        // Modify config file
        Thread.Sleep(50);
        File.WriteAllText(configPath, """
            {
                "OutputDir": "changed",
                "Templates": {}
            }
            """);

        // Reload config — should invalidate config, templates, AND schema
        _cache.GetConfiguration(configPath);

        // Schema file should still exist on disk
        _cache.HasModel(ModelPath).ShouldBeTrue();
        // Template cache should be invalidated
        _cache.IsTemplateStale(templatePath).ShouldBeTrue();
        // Schema cache should be invalidated — next GetSchema returns fresh objects
        var modelAfter = _cache.GetModel(ModelPath);
        modelAfter.ShouldNotBeNull();
        ReferenceEquals(modelBefore, modelAfter).ShouldBeFalse();
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
    public void GetModel_ReturnsNull_WhenNoModelFile()
    {
        _cache.GetModel(ModelPath).ShouldBeNull();
    }

    [Fact]
    public void GetModel_ReturnsCached_AfterSet()
    {
        var model = SampleModel();
        _cache.SetModel(model, ModelPath);

        var cached = _cache.GetModel(ModelPath);
        cached.ShouldNotBeNull();
        cached.Nodes.Count.ShouldBe(1);
        cached.Nodes[0].Name.ShouldBe("Products");
    }

    [Fact]
    public void SetModel_WritesModelJsonToDisk()
    {
        var model = SampleModel();
        _cache.SetModel(model, ModelPath);

        File.Exists(ModelPath).ShouldBeTrue();
        var json = File.ReadAllText(ModelPath);
        json.ShouldContain("Products");
        json.ShouldContain("Class");
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
