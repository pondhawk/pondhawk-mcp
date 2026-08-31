using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

/// <summary>
/// Renders one artifact and hands back the text.
/// </summary>
/// <remarks>
/// The authoring loop for a macro was generate, then open a file off disk — ceremony and
/// leftover artifacts for one answer. This is the tight loop. It plans exactly as a real run
/// would, filtered to a single template and node, and returns the rendered content instead of
/// writing it: same overrides, same variant resolution, same output path. A preview that took a
/// shortcut past overrides would show the wrong thing for precisely the nodes worth
/// investigating.
/// </remarks>
[McpServerToolType]
public sealed class PreviewTool
{
    [McpServerTool(Name = "preview"), Description("Renders one template for one node and returns the text without writing anything. Returns JSON: the rendered Content, the path it would have been written to, and the model and mode used. Overrides and variants apply exactly as in a real run. A render error comes back as Error rather than failing the call, since a half-finished macro is the normal state while authoring one. Use it to iterate on a template; use generate with dryRun to see what a change does across the whole project.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Template key to render.")]
        string template,
        [Description("Top-level node name to render. Omit it to render the first node the template matches, or — for a Single-scope template — all of them, which is what generate produces. Naming a node narrows a Single-scope render to just that node, the same way generate does with items; useful for inspecting one node's contribution, but not what the file will contain.")]
        string? node = null,
        [Description("Key-value pairs passed to the template context as {{ parameters.X }}")]
        Dictionary<string, object>? parameters = null)
    {
        var (logger, sw) = ctx.StartToolCall("preview", $"template={template}");
        var config = ctx.EnsureConfig();

        if (!config.Templates.TryGetValue(template, out var templateConfig))
        {
            return Failed(template, node,
                $"No template '{template}' is configured. Available: {string.Join(", ", config.Templates.Keys.Order())}");
        }

        GenerationPlan plan;
        try
        {
            // The planner already renders to memory and resolves output paths; filtered to one
            // template and one node it does exactly this job, so preview stays a view of the
            // real thing rather than a second rendering path that could drift from it.
            plan = GenerationPlanner.Build(
                ctx, config,
                templates: [template],
                items: node is null ? null : [node],
                parameters, logger);
        }
        catch (Exception ex)
        {
            // A template that will not compile, or a missing model. Both are ordinary states
            // mid-edit, and neither should read as the tool malfunctioning.
            return Failed(template, node, ex.Message);
        }

        if (plan.Failures.Count > 0)
            return Failed(template, node, plan.Failures[0].Error);

        if (plan.Files.Count == 0)
            return Failed(template, node, NothingMatched(ctx, config, templateConfig, template, node, plan));

        var file = plan.Files[0];

        sw.Stop();
        logger.LogInformation("Tool preview completed in {Duration}ms — {Template}/{Node}",
            sw.ElapsedMilliseconds, template, file.Reference);

        return JsonSerializer.Serialize(new
        {
            Template = template,
            Node = file.Reference,
            Model = file.ModelFile,
            file.Mode,
            OutputPath = file.RelativePath,
            NothingWritten = true,
            Lines = file.Content.Length == 0 ? 0 : file.Content.Split('\n').Length,
            file.Content,
            Note = string.IsNullOrWhiteSpace(file.Content)
                ? "This template renders empty for this node, so generate would skip the file rather than write it."
                : null
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Says why nothing rendered. The three reasons are different problems, and reporting an
    /// empty result for all of them would leave the caller guessing which one they have.
    /// </summary>
    private static string NothingMatched(
        ServerContext ctx, ProjectConfiguration config, TemplateConfig templateConfig,
        string template, string? node, GenerationPlan plan)
    {
        if (plan.DroppedByOverride > 0)
            return $"An Ignore override removes {node ?? "every matching node"} from artifact '{template}'.";

        var model = ctx.Cache.GetModel(Path.Combine(ctx.ProjectDir, templateConfig.ModelFile));
        var roots = model?.Nodes ?? [];

        if (node is not null && !roots.Any(n => n.Name.Equals(node, StringComparison.OrdinalIgnoreCase)))
        {
            var available = roots.Select(n => n.Name).Take(10).ToList();
            return $"No top-level node named '{node}' in {templateConfig.ModelFile}."
                   + (available.Count > 0 ? $" Available: {string.Join(", ", available)}" : " The model has no nodes.");
        }

        var appliesTo = string.IsNullOrEmpty(templateConfig.AppliesTo) ? "All" : templateConfig.AppliesTo;
        var kinds = roots.Select(n => n.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        return $"Template '{template}' applies to Kind '{appliesTo}', which matches no top-level node in "
               + $"{templateConfig.ModelFile}" + (kinds.Count > 0 ? $" (present: {string.Join(", ", kinds)})." : ".");
    }

    private static string Failed(string template, string? node, string error) =>
        JsonSerializer.Serialize(new
        {
            Template = template,
            Node = node,
            NothingWritten = true,
            Error = error
        }, new JsonSerializerOptions { WriteIndented = true });
}
