using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

[McpServerToolType]
public sealed class ListTemplatesTool
{
    [McpServerTool(Name = "list_templates"), Description("Lists the templates the project configuration declares. Returns JSON: each template key with its path, output pattern, scope, mode, the node Kind it applies to, and the model file it reads.")]
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
            kvp.Value.Mode,
            // An agent choosing which template to run needs to know which Kind it selects;
            // without it the listing cannot be matched against the nodes in the model.
            kvp.Value.AppliesTo,
            // Resolved rather than raw, so the listing always names the file to read.
            Model = kvp.Value.ModelFile
        }).ToList();

        sw.Stop();
        logger.LogInformation("Tool list_templates completed in {Duration}ms — {Count} templates", sw.ElapsedMilliseconds, templates.Count);

        return JsonSerializer.Serialize(new { Templates = templates });
    }
}
