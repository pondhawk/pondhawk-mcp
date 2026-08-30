using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;

// Parse --project argument
string? projectDir = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--project")
    {
        projectDir = args[i + 1];
        break;
    }
}

var checkOnly = args.Contains("--check");

if (string.IsNullOrEmpty(projectDir))
{
    Console.Error.WriteLine("Usage: pondhawk-generation-mcp --project <path> [--check]");
    Console.Error.WriteLine("  --check  Report whether generated files match the model, then exit.");
    Console.Error.WriteLine("           Exit code 0 clean, 1 not clean, 2 could not run.");
    return 1;
}

if (!Directory.Exists(projectDir))
{
    Console.Error.WriteLine($"Project directory does not exist: {projectDir}");
    return 1;
}

var ctx = new ServerContext(projectDir);

// Initialize logging early so the Serilog pipeline is ready for host DI registration
ctx.InitializeLogging();

// --check is a one-shot command rather than a server, so it returns before the host starts.
// Printing to stdout is only safe on this path: in server mode stdout carries the protocol.
if (checkOnly)
{
    return CheckCommand.Run(ctx, Console.Out, Console.Error);
}

var builder = Host.CreateApplicationBuilder(args);

// Replace default logging with Serilog so all logs (MCP SDK, third-party) flow through the same pipeline
builder.Logging.ClearProviders();
if (ctx.LoggingService.SerilogLogger is not null)
    builder.Logging.AddSerilog(ctx.LoggingService.SerilogLogger);

builder.Services.AddSingleton(ctx);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "pondhawk-generation",
            Version = ServerVersion.Current
        };

        // Sent in the initialize handshake. Without it a connecting agent is told only the
        // tool names, and has to infer the model/templates/config workflow from them.
        options.ServerInstructions = AgentGuide.ServerInstructions;
    })
    .WithStdioServerTransport()
    .WithTools<InitTool>()
    .WithTools<GenerateTool>()
    .WithTools<ListTemplatesTool>()
    .WithTools<ValidateConfigTool>()
    .WithTools<UpdateTool>()
    .WithTools<CheckTool>()
    .WithTools<PruneTool>()
    .WithTools<DescribeModelTool>()
    .WithTools<PreviewTool>()
    .WithResources<AgentGuideResource>();

var app = builder.Build();
await app.RunAsync();

return 0;
