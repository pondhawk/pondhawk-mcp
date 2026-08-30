using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

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
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{ item.Name }}.cs", Scope = "PerItem", Mode = "Always" },
                ["dbcontext"] = new() { Path = "templates/dbcontext.liquid", OutputPattern = "MyDbContext.cs", Scope = "Single", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);

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
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);

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
                ["entity"] = new() { Path = "templates/entity.liquid", OutputPattern = "Entities/{{ item.Name }}.cs", Scope = "PerItem", Mode = "SkipExisting" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);

        var ctx = new ServerContext(_tempDir);
        var result = ListTemplatesTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        var tmpl = json.RootElement.GetProperty("Templates")[0];
        tmpl.GetProperty("Key").GetString().ShouldBe("entity");
        tmpl.GetProperty("Path").GetString().ShouldBe("templates/entity.liquid");
        tmpl.GetProperty("Scope").GetString().ShouldBe("PerItem");
        tmpl.GetProperty("Mode").GetString().ShouldBe("SkipExisting");
    }

    [Fact]
    public void ListTemplates_ReportsTheKindEachTemplateAppliesTo()
    {
        // An agent picks a template by matching AppliesTo against the Kinds in the model,
        // so a listing without it cannot be acted on.
        var config = new ProjectConfiguration
        {
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new() { Path = "t.liquid", OutputPattern = "{{ item.Name }}.cs", Scope = "PerItem", Mode = "Always", AppliesTo = "Class" },
                ["index"] = new() { Path = "i.liquid", OutputPattern = "Index.cs", Scope = "Single", Mode = "Always" }
            }
        };
        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), config);

        var templates = JsonDocument.Parse(ListTemplatesTool.Execute(new ServerContext(_tempDir)))
            .RootElement.GetProperty("Templates");

        templates[0].GetProperty("AppliesTo").GetString().ShouldBe("Class");
        templates[1].GetProperty("AppliesTo").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
