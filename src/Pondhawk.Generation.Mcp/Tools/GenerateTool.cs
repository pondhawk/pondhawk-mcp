using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Models;
using Pondhawk.Generation.Rendering;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

[McpServerToolType]
public sealed class GenerateTool
{
    [McpServerTool(Name = "generate"), Description("Generates files by rendering Liquid templates against the nodes in model.json and writes them to disk. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Template keys to run (default: all)")]
        string[]? templates = null,
        [Description("Exact top-level node names to generate for (overrides a template's AppliesTo)")]
        string[]? items = null,
        [Description("Additional key-value pairs passed to the template context as {{ parameters.X }}")]
        Dictionary<string, object>? parameters = null)
    {
        var (logger, sw) = ctx.StartToolCall("generate");
        var config = ctx.EnsureConfig();

        var model = ctx.Cache.GetModel(ctx.ModelPath);
        if (model is null)
        {
            logger.LogError("Tool generate failed — model.json not found");
            throw new InvalidOperationException(
                "model.json not found. Write an input model describing the nodes to generate, then run generate again.");
        }

        var roots = model.Nodes;
        if (items is { Length: > 0 })
        {
            var names = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
            roots = roots.Where(n => names.Contains(n.Name)).ToList();
        }

        var templateEntries = config.Templates.AsEnumerable();
        if (templates is { Length: > 0 })
        {
            var keys = new HashSet<string>(templates, StringComparer.OrdinalIgnoreCase);
            templateEntries = templateEntries.Where(t => keys.Contains(t.Key));
        }

        var outputDir = Path.IsPathRooted(config.OutputDir)
            ? config.OutputDir
            : Path.Combine(ctx.ProjectDir, config.OutputDir);

        var filesWritten = new List<object>();
        int created = 0, overwritten = 0, skipped = 0, failed = 0;

        foreach (var (templateKey, templateConfig) in templateEntries)
        {
            var templatePath = Path.IsPathRooted(templateConfig.Path)
                ? templateConfig.Path
                : Path.Combine(ctx.ProjectDir, templateConfig.Path);

            IFluidTemplate compiledTemplate;
            try
            {
                compiledTemplate = ctx.Cache.GetTemplate(templatePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tool generate failed — could not compile template '{TemplateKey}'", templateKey);
                throw new InvalidOperationException($"Failed to compile template '{templateKey}': {ex.Message}", ex);
            }

            var artifactName = templateKey;
            var matching = roots.Where(n => MatchesAppliesTo(n, templateConfig.AppliesTo)).ToList();

            if (templateConfig.Scope.Equals("PerItem", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var node in matching)
                {
                    try
                    {
                        // Clone before overrides so per-artifact variants and metadata never
                        // leak into the next template or survive to the next generate call.
                        var resolved = OverrideResolver.Apply([node.Clone()], artifactName, config.Overrides);
                        if (resolved.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        var context = CreateContext(ctx, config, model, parameters, artifactName);
                        context.SetValue("item", FluidValue.Create(resolved[0], context.Options));

                        var content = ctx.TemplateEngine.Render(compiledTemplate, context);
                        var outputFileName = ResolveOutputPattern(ctx, templateConfig.OutputPattern, resolved[0]);
                        var result = FileWriter.WriteFile(Path.Combine(outputDir, outputFileName), content, templateConfig.Mode);

                        filesWritten.Add(new { Path = Path.GetRelativePath(outputDir, result.Path), result.Action });
                        Tally(result.Action, ref created, ref overwritten, ref skipped);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Tool generate — failed to render template '{TemplateKey}' for node '{NodeName}'", templateKey, node.Name);
                        filesWritten.Add(new { Path = $"{templateKey}/{node.Name}", Action = "Failed", Error = ex.Message });
                        failed++;
                    }
                }
            }
            else // Single
            {
                try
                {
                    var resolved = OverrideResolver.Apply(
                        matching.Select(n => n.Clone()).ToList(), artifactName, config.Overrides);

                    var context = CreateContext(ctx, config, model, parameters, artifactName);
                    context.SetValue("items", FluidValue.Create(resolved, context.Options));

                    var content = ctx.TemplateEngine.Render(compiledTemplate, context);
                    var outputFileName = ResolveOutputPattern(ctx, templateConfig.OutputPattern, null);
                    var result = FileWriter.WriteFile(Path.Combine(outputDir, outputFileName), content, templateConfig.Mode);

                    filesWritten.Add(new { Path = Path.GetRelativePath(outputDir, result.Path), result.Action });
                    Tally(result.Action, ref created, ref overwritten, ref skipped);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Tool generate — failed to render Single-scope template '{TemplateKey}'", templateKey);
                    filesWritten.Add(new { Path = templateKey, Action = "Failed", Error = ex.Message });
                    failed++;
                }
            }
        }

        var parts = new List<string>();
        if (overwritten > 0) parts.Add($"{overwritten} files written");
        if (created > 0) parts.Add($"{created} files created");
        if (skipped > 0) parts.Add($"{skipped} files skipped");
        if (failed > 0) parts.Add($"{failed} files failed");

        sw.Stop();
        logger.LogInformation("Tool generate completed in {Duration}ms — {Summary}", sw.ElapsedMilliseconds, string.Join(", ", parts));
        if (failed > 0)
            logger.LogWarning("Tool generate had {Failed} failures", failed);

        return JsonSerializer.Serialize(new
        {
            OutputDir = outputDir,
            FilesWritten = filesWritten,
            Summary = string.Join(", ", parts)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static TemplateContext CreateContext(
        ServerContext ctx,
        ProjectConfiguration config,
        ModelFile model,
        Dictionary<string, object>? parameters,
        string artifactName)
    {
        var context = ctx.TemplateEngine.CreateContext();
        context.SetValue("model", FluidValue.Create(model, context.Options));
        context.SetValue("values", FluidValue.Create(config.Values, context.Options));
        context.SetValue("config", FluidValue.Create(config, context.Options));
        if (parameters is not null)
            context.SetValue("parameters", FluidValue.Create(parameters, context.Options));
        context.AmbientValues["ArtifactName"] = artifactName;
        return context;
    }

    private static void Tally(string action, ref int created, ref int overwritten, ref int skipped)
    {
        switch (action)
        {
            case "Created": created++; break;
            case "Overwritten": overwritten++; break;
            case "SkippedExisting":
            case "SkippedEmpty": skipped++; break;
        }
    }

    private static string ResolveOutputPattern(ServerContext ctx, string pattern, Node? item)
    {
        if (!ctx.TemplateEngine.TryParse(pattern, out var tmpl, out _))
            return pattern;

        var renderCtx = ctx.TemplateEngine.CreateContext();
        if (item is not null)
            renderCtx.SetValue("item", FluidValue.Create(item, renderCtx.Options));

        return ctx.TemplateEngine.Render(tmpl, renderCtx).Trim();
    }

    /// <summary>
    /// A template with no AppliesTo, or "All", renders every top-level node; otherwise it is
    /// restricted to nodes of that Kind.
    /// </summary>
    private static bool MatchesAppliesTo(Node node, string? appliesTo)
        => string.IsNullOrEmpty(appliesTo)
           || appliesTo.Equals("All", StringComparison.OrdinalIgnoreCase)
           || appliesTo.Equals(node.Kind, StringComparison.OrdinalIgnoreCase);
}
