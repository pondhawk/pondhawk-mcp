using System.Text.Json;
using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Persistence.Mcp.Tests.Tools;

public class InitToolTests : IDisposable
{
    private readonly string _tempDir;

    public InitToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_init_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Init_CreatesAllExpectedFiles()
    {
        var ctx = new ServerContext(_tempDir);
        var result = InitTool.Execute(ctx);
        var json = JsonDocument.Parse(result);

        json.RootElement.GetProperty("FilesCreated").GetArrayLength().ShouldBe(7);
        File.Exists(Path.Combine(_tempDir, "persistence.project.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "persistence.project.schema.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "db-design.schema.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "AGENTS.md")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, ".env")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "templates", "entity.generated.liquid")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "templates", "entity.stub.liquid")).ShouldBeTrue();
    }

    [Fact]
    public void Init_ThrowsError_WhenConfigExists()
    {
        File.WriteAllText(Path.Combine(_tempDir, "persistence.project.json"), "{}");
        var ctx = new ServerContext(_tempDir);

        var ex = Should.Throw<InvalidOperationException>(() => InitTool.Execute(ctx));
        ex.Message.ShouldContain("already exists");
    }

    [Fact]
    public void Init_AppliesProviderParameter()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx, provider: "postgresql");

        var config = File.ReadAllText(Path.Combine(_tempDir, "persistence.project.json"));
        config.ShouldContain("postgresql");
    }

    [Fact]
    public void Init_AppliesNamespaceParameter()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx, @namespace: "Acme.Data");

        var config = File.ReadAllText(Path.Combine(_tempDir, "persistence.project.json"));
        config.ShouldContain("Acme.Data");
    }

    [Fact]
    public void Init_GeneratedTemplatesAreValidLiquid()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx);

        var entityTemplate = File.ReadAllText(Path.Combine(_tempDir, "templates", "entity.generated.liquid"));
        var engine = new Pondhawk.Persistence.Core.Rendering.TemplateEngine();
        engine.TryParse(entityTemplate, out _, out var error).ShouldBeTrue(error);
    }

    [Fact]
    public void Init_CreatesEnvFileWithProviderPlaceholder()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx, provider: "postgresql");

        var envContent = File.ReadAllText(Path.Combine(_tempDir, ".env"));
        envContent.ShouldContain("DB_CONNECTION=");
        envContent.ShouldContain("Host=localhost");
    }

    [Fact]
    public void Init_ConnectionStringParam_WrittenToEnvFile()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx, provider: "sqlserver", connectionString: "Server=prod;Database=MyDb;User=sa;Password=secret");

        var envContent = File.ReadAllText(Path.Combine(_tempDir, ".env"));
        envContent.ShouldContain("DB_CONNECTION=Server=prod;Database=MyDb;User=sa;Password=secret");
    }

    [Fact]
    public void Init_NoConnectionString_UsesPlaceholder()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx, provider: "sqlite");

        var envContent = File.ReadAllText(Path.Combine(_tempDir, ".env"));
        envContent.ShouldContain("DB_CONNECTION=Data Source=");
    }

    [Fact]
    public void Init_ConfigIncludesLoggingSectionDisabled()
    {
        var ctx = new ServerContext(_tempDir);
        InitTool.Execute(ctx);

        var config = File.ReadAllText(Path.Combine(_tempDir, "persistence.project.json"));
        config.ShouldContain("Logging");
        config.ShouldContain("\"Enabled\": false");
    }
}
