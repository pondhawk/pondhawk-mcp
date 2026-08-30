using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

[McpServerToolType]
public sealed class DescribeModelTool
{
    [McpServerTool(Name = "describe_model"), Description("Summarises a model's conventions without listing its nodes: the Kind vocabulary with counts and examples, which Kinds nest inside which, the metadata keys each Kind carries and how many nodes carry them, and notices about inconsistencies. Read this before extending a model — it is how you find the Kinds and metadata keys already in use rather than inventing a second set. Returns JSON, one description per model.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Model file to describe, relative to the project. Default: every model the templates reference.")]
        string? model = null)
    {
        var (logger, sw) = ctx.StartToolCall("describe_model", model is null ? null : $"model={model}");
        var config = ctx.EnsureConfig();

        var files = model is not null ? [model] : Referenced(config);
        var descriptions = new List<ModelDescription>();
        var missing = new List<string>();

        foreach (var file in files)
        {
            var loaded = ctx.Cache.GetModel(Path.Combine(ctx.ProjectDir, file));
            if (loaded is null)
                missing.Add(file);
            else
                descriptions.Add(ModelDescriber.Describe(loaded, file));
        }

        sw.Stop();
        logger.LogInformation("Tool describe_model completed in {Duration}ms — {Count} models described",
            sw.ElapsedMilliseconds, descriptions.Count);

        return JsonSerializer.Serialize(new
        {
            Models = descriptions,
            NotFound = missing,
            Summary = descriptions.Count == 0
                ? $"No model found ({string.Join(", ", files)})"
                : string.Join("; ", descriptions.Select(d =>
                    $"{d.Model}: {d.TotalNodes} nodes, {d.Kinds.Count} Kinds"
                    + (d.Notices.Count > 0 ? $", {d.Notices.Count} notices" : "")))
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Every model the templates read, plus the default. Saves the caller needing to know the
    /// filenames of a project it has not read yet — which is the position this tool exists for.
    /// </summary>
    private static List<string> Referenced(ProjectConfiguration config) =>
        config.Templates.Values
            .Select(t => t.ModelFile)
            .Append(TemplateConfig.DefaultModelFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
