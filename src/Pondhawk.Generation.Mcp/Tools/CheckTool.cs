using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Generation.Manifest;
using Pondhawk.Generation.Rendering;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

[McpServerToolType]
public sealed class CheckTool
{
    [McpServerTool(Name = "check"), Description("Reports whether the generated files on disk are what the current model and templates produce, without writing anything. Returns JSON: UpToDate, plus every stale file and why. Use it after pulling a branch or before relying on generated code; run generate with dryRun to see the actual diffs.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Template keys to check (default: all)")]
        string[]? templates = null)
    {
        var (logger, sw) = ctx.StartToolCall("check");
        var config = ctx.EnsureConfig();

        var plan = GenerationPlanner.Build(ctx, config, templates, items: null, parameters: null, logger);
        var manifest = ManifestStore.Load(ctx.ProjectDir);

        var stale = new List<object>();
        var checkedCount = 0;

        foreach (var file in plan.Files)
        {
            var outcome = FileWriter.Decide(file.FullPath, file.Content, file.Mode, compareContent: true);

            switch (outcome)
            {
                case WriteOutcome.Unchanged:
                    checkedCount++;
                    break;

                case WriteOutcome.Create:
                    // Missing is drift for either mode: an Always file has not been generated,
                    // and a SkipExisting stub the developer owns has gone.
                    checkedCount++;
                    stale.Add(new { file.RelativePath, Reason = "Missing", file.TemplateKey, Detail = "No file at this path." });
                    break;

                case WriteOutcome.Overwrite:
                    checkedCount++;
                    stale.Add(Diagnose(file, manifest));
                    break;

                // A SkipExisting file that is present belongs to the developer — generate would
                // not touch it, so it cannot be stale. An empty render writes nothing either.
                case WriteOutcome.SkippedExisting:
                case WriteOutcome.Empty:
                default:
                    break;
            }
        }

        // Orphan detection needs to know everything the project produces, so a filtered check
        // cannot do it — every template it did not run would look like it produces nothing.
        var orphans = templates is { Length: > 0 }
            ? []
            : Orphans(plan, manifest);

        var failures = plan.Failures
            .Select(f => new { f.Reference, f.Error })
            .ToList();

        var upToDate = stale.Count == 0 && failures.Count == 0 && orphans.Count == 0;

        var summary = upToDate
            ? $"Up to date — {checkedCount} generated files match the model"
            : string.Join(", ", new[]
            {
                failures.Count > 0 ? $"{failures.Count} files FAILED to render" : null,
                stale.Count > 0 ? $"{stale.Count} of {checkedCount} files are stale" : null,
                orphans.Count > 0 ? $"{orphans.Count} orphaned files no longer produced (run prune)" : null
            }.Where(p => p is not null));

        sw.Stop();
        logger.LogInformation("Tool check completed in {Duration}ms — {Summary}", sw.ElapsedMilliseconds, summary);
        if (!upToDate)
            logger.LogWarning("Tool check found {Stale} stale files and {Failed} render failures", stale.Count, failures.Count);

        return JsonSerializer.Serialize(new
        {
            UpToDate = upToDate,
            Checked = checkedCount,
            Stale = stale,
            Orphans = orphans,
            Failed = failures,
            OutputDir = plan.OutputDir,
            Summary = summary
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Says why a file differs, which decides what to do about it. "The model moved on" and
    /// "somebody edited generated output" both look identical to a content comparison, and they
    /// want opposite responses — regenerate one, rescue the other.
    /// </summary>
    private static object Diagnose(PlannedFile file, GenerationManifest manifest)
    {
        if (!manifest.Files.TryGetValue(file.RelativePath, out var entry))
        {
            return new
            {
                file.RelativePath,
                Reason = "Differs",
                file.TemplateKey,
                Detail = "No manifest entry — pondhawk has no record of writing this file, so it cannot tell an edit from a stale generation."
            };
        }

        var onDisk = ManifestStore.HashFile(file.FullPath);

        return onDisk == entry.Hash
            ? new
            {
                file.RelativePath,
                Reason = "InputsChanged",
                file.TemplateKey,
                Detail = "The file is exactly as pondhawk last wrote it; the model or template changed. Safe to regenerate."
            }
            : new
            {
                file.RelativePath,
                Reason = "EditedSinceGenerated",
                file.TemplateKey,
                Detail = "The file has changed since pondhawk wrote it. Regenerating will discard those edits — move them into the template or a SkipExisting file first."
            };
    }

    /// <summary>
    /// Files the manifest records but the current configuration no longer produces. An entry
    /// whose file is already gone is not reported: there is nothing left to clean up.
    /// </summary>
    private static List<object> Orphans(GenerationPlan plan, GenerationManifest manifest)
    {
        var produced = plan.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);

        return manifest.Files
            .Where(kvp => !produced.Contains(kvp.Key))
            .Where(kvp => File.Exists(Path.Combine(plan.OutputDir, kvp.Key)))
            .Select(kvp => (object)new
            {
                RelativePath = kvp.Key,
                kvp.Value.Template,
                kvp.Value.Node,
                kvp.Value.Mode
            })
            .ToList();
    }
}
