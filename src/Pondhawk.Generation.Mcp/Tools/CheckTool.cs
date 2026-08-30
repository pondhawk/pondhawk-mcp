using System.ComponentModel;
using System.Text.Json;
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
                    stale.Add(new { file.RelativePath, Reason = "Missing", file.TemplateKey });
                    break;

                case WriteOutcome.Overwrite:
                    checkedCount++;
                    stale.Add(new { file.RelativePath, Reason = "Differs", file.TemplateKey });
                    break;

                // A SkipExisting file that is present belongs to the developer — generate would
                // not touch it, so it cannot be stale. An empty render writes nothing either.
                case WriteOutcome.SkippedExisting:
                case WriteOutcome.Empty:
                default:
                    break;
            }
        }

        var failures = plan.Failures
            .Select(f => new { f.Reference, f.Error })
            .ToList();

        var upToDate = stale.Count == 0 && failures.Count == 0;

        var summary = upToDate
            ? $"Up to date — {checkedCount} generated files match the model"
            : string.Join(", ", new[]
            {
                failures.Count > 0 ? $"{failures.Count} files FAILED to render" : null,
                stale.Count > 0 ? $"{stale.Count} of {checkedCount} files are stale" : null
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
            Failed = failures,
            OutputDir = plan.OutputDir,
            Summary = summary
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
