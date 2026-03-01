using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class ListTemplatesToolTests : IDisposable
{
    private readonly string _tempDir;

    public ListTemplatesToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_lt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ListTemplates_ReturnsAllTemplates()
    {
        var config = new ProjectConfiguration
        {
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{entity.Name}}.cs", Scope = "PerModel", Mode = "Always" },
                ["dbcontext"] = new() { Path = "templates/dbcontext.liquid", OutputPattern = "MyDbContext.cs", Scope = "SingleFile", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ListTemplatesTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Templates").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void ListTemplates_EmptyTemplates_ReturnsEmptyArray()
    {
        var config = new ProjectConfiguration
        {
            Templates = new Dictionary<string, TemplateConfig>()
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ListTemplatesTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("Templates").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void ListTemplates_ReturnsCorrectFields()
    {
        var config = new ProjectConfiguration
        {
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{entity.Name}}.cs", Scope = "PerModel", Mode = "SkipExisting" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "persistence.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ListTemplatesTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var tmpl = json.RootElement.GetProperty("Templates")[0];
        tmpl.GetProperty("Key").GetString().ShouldBe("entity");
        tmpl.GetProperty("Path").GetString().ShouldBe("templates/entity.liquid");
        tmpl.GetProperty("Scope").GetString().ShouldBe("PerModel");
        tmpl.GetProperty("Mode").GetString().ShouldBe("SkipExisting");
    }
}
