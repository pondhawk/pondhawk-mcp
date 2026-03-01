using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class ValidateConfigTool
{
    [McpServerTool(Name = "validate_config"), Description("Validates the project's persistence.project.json and its referenced templates without connecting to any database. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(ServerContext ctx)
    {
        var (logger, sw) = ctx.StartToolCall("validate_config");

        if (!File.Exists(ctx.ConfigPath))
            return JsonSerializer.Serialize(new { Valid = false, Errors = new[] { "persistence.project.json not found." }, Warnings = Array.Empty<string>() });

        var rawJson = File.ReadAllText(ctx.ConfigPath);
        var config = ctx.EnsureConfig();
        var result = ConfigurationValidator.Validate(rawJson, config, ctx.ProjectDir);

        sw.Stop();
        logger.LogInformation("Tool validate_config completed in {Duration}ms — valid={Valid}, {Errors} errors, {Warnings} warnings",
            sw.ElapsedMilliseconds, result.Errors.Count == 0, result.Errors.Count, result.Warnings.Count);

        return JsonSerializer.Serialize(new
        {
            Valid = result.Errors.Count == 0,
            result.Errors,
            result.Warnings
        });
    }
}
