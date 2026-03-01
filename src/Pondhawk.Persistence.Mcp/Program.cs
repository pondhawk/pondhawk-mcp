using Pondhawk.Persistence.Mcp;
using Pondhawk.Persistence.Mcp.Tools;
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

if (string.IsNullOrEmpty(projectDir))
{
    Console.Error.WriteLine("Usage: pondhawk-mcp --project <path>");
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
            Name = "pondhawk-mcp",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithTools<InitTool>()
    .WithTools<IntrospectSchemaTool>()
    .WithTools<GenerateTool>()
    .WithTools<ListTemplatesTool>()
    .WithTools<ValidateConfigTool>()
    .WithTools<UpdateTool>()
    .WithTools<GenerateDdlTool>()
    .WithTools<GenerateDiagramTool>()
    .WithTools<GenerateMigrationTool>();

var app = builder.Build();
await app.RunAsync();

return 0;
