using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class ListTemplatesTool
{
    [McpServerTool(Name = "list_templates"), Description("Lists available templates from the project configuration. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(ServerContext ctx)
    {
        var (logger, sw) = ctx.StartToolCall("list_templates");
        var config = ctx.EnsureConfig();

        var templates = config.Templates.Select(kvp => new
        {
            Key = kvp.Key,
            kvp.Value.Path,
            kvp.Value.OutputPattern,
            kvp.Value.Scope,
            kvp.Value.Mode
        }).ToList();

        sw.Stop();
        logger.LogInformation("Tool list_templates completed in {Duration}ms — {Count} templates", sw.ElapsedMilliseconds, templates.Count);

        return JsonSerializer.Serialize(new { Templates = templates });
    }
}
