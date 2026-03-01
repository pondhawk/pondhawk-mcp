using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class UpdateTool
{
    [McpServerTool(Name = "update"), Description("Updates AGENTS.md and persistence.project.schema.json to the latest version, and normalizes persistence.project.json. Run after upgrading pondhawk-mcp.")]
    public static string Execute(ServerContext ctx)
    {
        var (logger, sw) = ctx.StartToolCall("update");

        if (!File.Exists(ctx.ConfigPath))
        {
            logger.LogError("Tool update failed — persistence.project.json not found");
            throw new InvalidOperationException("persistence.project.json not found. Run the init tool first to create a project.");
        }

        var utf8NoBom = new UTF8Encoding(false);
        var filesUpdated = new List<string>();

        // Overwrite AGENTS.md with latest embedded content
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "AGENTS.md"), InitTool.GetAgentsMarkdown(), utf8NoBom);
        filesUpdated.Add("AGENTS.md");

        // Overwrite JSON Schemas with latest version
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "persistence.project.schema.json"), ProjectConfigurationSchema.SchemaJson, utf8NoBom);
        filesUpdated.Add("persistence.project.schema.json");
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "db-design.schema.json"), DbDesignFileSchema.SchemaJson, utf8NoBom);
        filesUpdated.Add("db-design.schema.json");

        // Normalize config: load → save (round-trip picks up new defaults, drops unknown fields)
        var config = ctx.EnsureConfig();
        ProjectConfigurationLoader.Save(ctx.ConfigPath, config);
        ctx.Cache.UpdateConfigTimestampAfterWriteBack(ctx.ConfigPath);
        filesUpdated.Add("persistence.project.json");

        sw.Stop();
        logger.LogInformation("Tool update completed in {Duration}ms — {FileCount} files updated", sw.ElapsedMilliseconds, filesUpdated.Count);

        return JsonSerializer.Serialize(new
        {
            FilesUpdated = filesUpdated,
            Message = "Project files updated to the latest version. AGENTS.md and JSON Schema are current; config has been normalized."
        });
    }
}
