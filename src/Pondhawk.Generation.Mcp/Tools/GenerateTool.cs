using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Manifest;
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
    [McpServerTool(Name = "generate"), Description("Renders the configured Liquid templates against the nodes in their model and writes the files to disk. Returns JSON: Success, per-file Created/Overwritten/Skipped/Failed counts, and the failures themselves. Generation is per-file, so check the counts rather than assuming a returned result means everything was written. Pass dryRun to see what would change — unified diffs, nothing written — before committing to a run.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Template keys to run (default: all)")]
        string[]? templates = null,
        [Description("Exact top-level node names to generate for (overrides a template's AppliesTo)")]
        string[]? items = null,
        [Description("Additional key-value pairs passed to the template context as {{ parameters.X }}")]
        Dictionary<string, object>? parameters = null,
        [Description("Render and report what would change without writing anything. Returns WouldCreate/WouldOverwrite/Unchanged/WouldSkip counts and a unified diff for each file whose content would change.")]
        bool dryRun = false)
    {
        var (logger, sw) = ctx.StartToolCall("generate", dryRun ? "dryRun=true" : null);
        var config = ctx.EnsureConfig();

        var plan = GenerationPlanner.Build(ctx, config, templates, items, parameters, logger);

        return dryRun
            ? Preview(plan, sw, logger)
            : Write(ctx, plan, sw, logger);
    }

    private static string Write(ServerContext ctx, GenerationPlan plan, Stopwatch sw, ILogger logger)
    {
        var manifest = ManifestStore.Load(ctx.ProjectDir);
        manifest.OutputDir = plan.ConfiguredOutputDir;
        var filesWritten = new List<object>();
        int created = 0, overwritten = 0, unchanged = 0, skipped = plan.DroppedByOverride, failed = plan.Failures.Count;

        foreach (var file in plan.Files)
        {
            try
            {
                var result = FileWriter.WriteResolved(file.FullPath, file.Content, file.Mode);
                filesWritten.Add(new { file.RelativePath, result.Action });
                Tally(result.Action, ref created, ref overwritten, ref unchanged, ref skipped);
                Record(manifest, plan, file, result.Action);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tool generate — failed to write '{Path}'", file.RelativePath);
                filesWritten.Add(new { file.RelativePath, Action = "Failed", Error = ex.Message });
                failed++;
            }
        }

        foreach (var failure in plan.Failures)
            filesWritten.Add(new { RelativePath = failure.Reference, Action = "Failed", failure.Error });

        // Failures lead: a caller skimming the summary should not have to reach the end of
        // the sentence to discover the run produced nothing but errors.
        var parts = new List<string>();
        if (failed > 0) parts.Add($"{failed} files FAILED");
        if (overwritten > 0) parts.Add($"{overwritten} files written");
        if (created > 0) parts.Add($"{created} files created");
        if (skipped > 0) parts.Add($"{skipped} files skipped");
        if (unchanged > 0) parts.Add($"{unchanged} files already current");
        if (parts.Count == 0) parts.Add("nothing to generate — no nodes matched any template");

        ManifestStore.Save(ctx.ProjectDir, manifest);

        sw.Stop();
        logger.LogInformation("Tool generate completed in {Duration}ms — {Summary}", sw.ElapsedMilliseconds, string.Join(", ", parts));
        if (failed > 0)
            logger.LogWarning("Tool generate had {Failed} failures", failed);

        return JsonSerializer.Serialize(new
        {
            Success = failed == 0,
            Created = created,
            Overwritten = overwritten,
            Unchanged = unchanged,
            Skipped = skipped,
            Failed = failed,
            OutputDir = plan.OutputDir,
            FilesWritten = filesWritten,
            Summary = string.Join(", ", parts)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Records provenance for a file this run actually wrote.
    /// </summary>
    /// <remarks>
    /// Only files pondhawk wrote are recorded, so the stored hash always answers "what did we
    /// last put here" — the question hand-edit detection depends on. A SkipExisting file that
    /// already existed is left alone: it is the developer's, this run did not write it, and
    /// stamping our hash on it would erase the evidence that it diverged. Unchanged files are
    /// recorded because their content is ours by definition, which also adopts files generated
    /// before this project had a manifest.
    /// </remarks>
    private static void Record(GenerationManifest manifest, GenerationPlan plan, PlannedFile file, string action)
    {
        if (action is not ("Created" or "Overwritten" or "Unchanged"))
            return;

        manifest.Files[file.RelativePath] = new ManifestEntry
        {
            Template = file.TemplateKey,
            Node = file.Reference,
            Model = file.ModelFile,
            Mode = file.Mode,
            Hash = ManifestStore.HashContent(file.Content)
        };
    }

    /// <summary>
    /// Reports what a run would do. The counts use "Would" names and the file list is called
    /// FilesPlanned so that a dry-run result can never be mistaken for a completed one — the
    /// same reason the real run reports failures rather than a bare success.
    /// </summary>
    private static string Preview(GenerationPlan plan, Stopwatch sw, ILogger logger)
    {
        var files = new List<object>();
        int create = 0, overwrite = 0, unchanged = 0, skipped = plan.DroppedByOverride;

        foreach (var file in plan.Files)
        {
            var outcome = FileWriter.Decide(file.FullPath, file.Content, file.Mode, compareContent: true);

            switch (outcome)
            {
                case WriteOutcome.Create:
                    create++;
                    files.Add(new { file.RelativePath, Action = "WouldCreate", Lines = LineCount(file.Content) });
                    break;

                case WriteOutcome.Overwrite:
                    overwrite++;
                    files.Add(new
                    {
                        file.RelativePath,
                        Action = "WouldOverwrite",
                        Diff = UnifiedDiff.Create(File.ReadAllText(file.FullPath), file.Content, file.RelativePath)
                    });
                    break;

                case WriteOutcome.Unchanged:
                    unchanged++;
                    files.Add(new { file.RelativePath, Action = "Unchanged" });
                    break;

                default:
                    skipped++;
                    files.Add(new
                    {
                        file.RelativePath,
                        Action = outcome == WriteOutcome.Empty ? "WouldSkipEmpty" : "WouldSkipExisting"
                    });
                    break;
            }
        }

        foreach (var failure in plan.Failures)
            files.Add(new { RelativePath = failure.Reference, Action = "Failed", failure.Error });

        var parts = new List<string>();
        if (plan.Failures.Count > 0) parts.Add($"{plan.Failures.Count} files FAILED to render");
        if (overwrite > 0) parts.Add($"{overwrite} files would change");
        if (create > 0) parts.Add($"{create} files would be created");
        if (skipped > 0) parts.Add($"{skipped} files would be skipped");
        if (unchanged > 0) parts.Add($"{unchanged} files already current");
        if (parts.Count == 0) parts.Add("nothing to generate — no nodes matched any template");

        sw.Stop();
        logger.LogInformation("Tool generate (dry run) completed in {Duration}ms — {Summary}", sw.ElapsedMilliseconds, string.Join(", ", parts));

        return JsonSerializer.Serialize(new
        {
            DryRun = true,
            NothingWritten = true,
            Success = plan.Failures.Count == 0,
            WouldCreate = create,
            WouldOverwrite = overwrite,
            Unchanged = unchanged,
            WouldSkip = skipped,
            Failed = plan.Failures.Count,
            OutputDir = plan.OutputDir,
            FilesPlanned = files,
            Summary = string.Join(", ", parts)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static int LineCount(string content) =>
        content.Length == 0 ? 0 : content.Split('\n').Length;

    internal static TemplateContext CreateContext(
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

    private static void Tally(string action, ref int created, ref int overwritten, ref int unchanged, ref int skipped)
    {
        switch (action)
        {
            case "Created": created++; break;
            case "Overwritten": overwritten++; break;
            case "Unchanged": unchanged++; break;
            case "SkippedExisting":
            case "SkippedEmpty": skipped++; break;
        }
    }

    internal static string ResolveOutputPattern(ServerContext ctx, string pattern, Node? item)
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
}
