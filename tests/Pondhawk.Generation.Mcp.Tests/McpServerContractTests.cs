using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Pondhawk.Generation.Mcp;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests;

/// <summary>
/// Drives the real server over stdio with a real MCP client.
/// </summary>
/// <remarks>
/// The unit tests prove the instruction text exists; only a live handshake proves it is
/// wired into the protocol. Delete <c>ServerInstructions</c> or
/// <c>WithResources&lt;AgentGuideResource&gt;()</c> from Program.cs and every unit test
/// still passes while an agent connecting to the server learns nothing — which is the exact
/// state this change set out to fix.
/// </remarks>
/// <summary>
/// Starts the server once for the whole class. Each test only reads, and a stdio server
/// costs a process launch, so there is nothing to gain from one per test.
/// </summary>
public sealed class McpServerFixture : IAsyncLifetime
{
    public McpClient Client { get; private set; } = null!;
    public string ProjectDir { get; } =
        Path.Combine(Path.GetTempPath(), $"pondhawk_mcp_{Guid.NewGuid():N}");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(ProjectDir);

        // The server is a ProjectReference, so its apphost sits beside the test binary.
        var exe = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "pondhawk-generation-mcp.exe" : "pondhawk-generation-mcp");

        File.Exists(exe).ShouldBeTrue($"Server executable not found at {exe}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "contract-tests",
                Command = exe,
                Arguments = ["--project", ProjectDir]
            }),
            cancellationToken: cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        if (Directory.Exists(ProjectDir))
            Directory.Delete(ProjectDir, true);
    }
}

public class McpServerContractTests(McpServerFixture server) : IClassFixture<McpServerFixture>
{
    private McpClient _client => server.Client;
    private string _tempDir => server.ProjectDir;

    // --- The handshake -------------------------------------------------------

    [Fact]
    public void Handshake_CarriesTheServerInstructions()
    {
        _client.ServerInstructions.ShouldNotBeNullOrWhiteSpace();
        _client.ServerInstructions.ShouldBe(AgentGuide.ServerInstructions);
    }

    [Fact]
    public void Handshake_IdentifiesTheServer()
    {
        _client.ServerInfo.Name.ShouldBe("pondhawk-generation");
    }

    // --- The guide resource --------------------------------------------------

    [Fact]
    public async Task ListResources_AdvertisesTheAgentGuide()
    {
        var resources = await _client.ListResourcesAsync();

        var guide = resources.ShouldHaveSingleItem();
        guide.Uri.ShouldBe(AgentGuide.ResourceUri);
        guide.MimeType.ShouldBe("text/markdown");
    }

    [Fact]
    public async Task ReadResource_ReturnsTheFullGuide()
    {
        var result = await _client.ReadResourceAsync(AgentGuide.ResourceUri);

        var text = result.Contents.OfType<TextResourceContents>().ShouldHaveSingleItem();
        text.Text.ShouldBe(AgentGuide.Markdown);
    }

    [Fact]
    public async Task ReadResource_WorksBeforeTheProjectIsInitialized()
    {
        // _tempDir is empty — no init has run, so there is no AGENTS.md on disk. The guide
        // still has to be readable, because an agent needs it most before setting up.
        File.Exists(Path.Combine(_tempDir, "AGENTS.md")).ShouldBeFalse();

        var result = await _client.ReadResourceAsync(AgentGuide.ResourceUri);

        result.Contents.OfType<TextResourceContents>().ShouldHaveSingleItem()
            .Text.ShouldContain("Instructions for AI Agents");
    }

    // --- Tools ---------------------------------------------------------------

    [Fact]
    public async Task ListTools_ExposesEveryToolWithAUsableDescription()
    {
        var tools = await _client.ListToolsAsync();

        tools.Select(t => t.Name).ShouldBe(
            ["init", "generate", "list_templates", "validate_config", "update"],
            ignoreOrder: true);

        foreach (var tool in tools)
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace();
            tool.Description!.ShouldNotContain("See AGENTS.md");
        }
    }
}
