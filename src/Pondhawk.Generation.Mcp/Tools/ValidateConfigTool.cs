using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

[McpServerToolType]
public sealed class ValidateConfigTool
{
    [McpServerTool(Name = "validate_config"), Description("Checks pondhawk.project.json, the templates it references, and model.json without writing anything. Returns JSON: Valid, plus errors and warnings. Catches unparseable templates, unknown filters, a model that violates its schema, overrides matching no node, a Kind nested under what a template renders that has no macro to render it, and an override naming a variant macro no template declares. Run it after editing the model or templates.")]
    public static string Execute(ServerContext ctx)
    {
        var (logger, sw) = ctx.StartToolCall("validate_config");

        if (!File.Exists(ctx.ConfigPath))
            return JsonSerializer.Serialize(new { Valid = false, Errors = new[] { "pondhawk.project.json not found. Run init to scaffold a project." }, Warnings = Array.Empty<string>() });

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
