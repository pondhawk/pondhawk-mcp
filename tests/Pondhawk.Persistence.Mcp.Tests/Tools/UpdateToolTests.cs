using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class UpdateToolTests : IDisposable
{
    private readonly string _tempDir;

    public UpdateToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_update_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Update_RefreshesAgentsAndSchema()
    {
        // Init the project first
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx);

        // Tamper with AGENTS.md and schema.json to simulate stale files
        File.WriteAllText(Path.Combine(_tempDir, "AGENTS.md"), "old content");
        File.WriteAllText(Path.Combine(_tempDir, "persistence.project.schema.json"), "{}");

        // Run update
        ctx = new ServerContext(_tempDir);
        var result = UpdateTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("FilesUpdated").GetArrayLength().ShouldBe(4);

        // AGENTS.md should contain current content (not the tampered "old content")
        var agentsContent = File.ReadAllText(Path.Combine(_tempDir, "AGENTS.md"));
        agentsContent.ShouldContain("pondhawk-mcp");
        agentsContent.ShouldContain("update");
        agentsContent.ShouldNotBe("old content");

        // Schema file should match current embedded schema
        var schemaContent = File.ReadAllText(Path.Combine(_tempDir, "persistence.project.schema.json"));
        schemaContent.ShouldBe(ProjectConfigurationSchema.SchemaJson);
    }

    [Fact]
    public void Update_NormalizesConfig()
    {
        // Init the project
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx);

        // Manually add AppliesTo to a template in the config
        ctx = new ServerContext(_tempDir);
        var config = ctx.EnsureConfig();
        config.Templates["entity"].AppliesTo = "Tables";
        ProjectConfigurationLoader.Save(ctx.ConfigPath, config);

        // Run update (normalizes config via round-trip)
        ctx = new ServerContext(_tempDir);
        UpdateTool.Execute(ctx);

        // Verify AppliesTo survived the round-trip
        ctx = new ServerContext(_tempDir);
        var reloaded = ctx.EnsureConfig();
        reloaded.Templates["entity"].AppliesTo.ShouldBe("Tables");
    }

    [Fact]
    public void Update_ThrowsWhenNoConfig()
    {
        var ctx = new ServerContext(_tempDir);

        var ex = Should.Throw<InvalidOperationException>(() => UpdateTool.Execute(ctx));
        ex.Message.ShouldContain("init");
    }

    [Fact]
    public void Update_PreservesExistingConfigValues()
    {
        // Init the project
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx);

        // Add TypeMappings and Overrides to the config
        ctx = new ServerContext(_tempDir);
        var config = ctx.EnsureConfig();
        config.TypeMappings =
        [
            new TypeMappingConfig { DbType = "decimal", ClrType = "decimal" }
        ];
        config.Overrides =
        [
            new OverrideConfig { Class = "Orders", Property = "Total", DataType = "Money" }
        ];
        ProjectConfigurationLoader.Save(ctx.ConfigPath, config);

        // Run update
        ctx = new ServerContext(_tempDir);
        UpdateTool.Execute(ctx);

        // Verify TypeMappings and Overrides survived
        ctx = new ServerContext(_tempDir);
        var reloaded = ctx.EnsureConfig();
        reloaded.TypeMappings.Count.ShouldBe(1);
        reloaded.TypeMappings[0].DbType.ShouldBe("decimal");
        reloaded.TypeMappings[0].ClrType.ShouldBe("decimal");
        reloaded.Overrides.ShouldNotBeEmpty();
        reloaded.Overrides.Count.ShouldBe(1);
        reloaded.Overrides[0].Class.ShouldBe("Orders");
        reloaded.Overrides[0].Property.ShouldBe("Total");
        reloaded.Overrides[0].DataType.ShouldBe("Money");
    }
}
