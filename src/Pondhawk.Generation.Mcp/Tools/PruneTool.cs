using System.ComponentModel;
using System.Text.Json;
using Pondhawk.Generation.Manifest;
using Pondhawk.Generation.Rendering;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

/// <summary>
/// Removes files pondhawk generated and no longer generates.
/// </summary>
/// <remarks>
/// A separate tool rather than a flag on generate, because deleting source files is a different
/// verb with different consequences and burying it in a boolean is how accidents happen. It
/// reports by default and deletes only when told to.
/// </remarks>
[McpServerToolType]
public sealed class PruneTool
{
    [McpServerTool(Name = "prune"), Description("Finds generated files the current model and templates no longer produce, and optionally deletes them. Reports by default and writes nothing; pass apply to actually delete. Returns JSON: files removed, and files deliberately kept with the reason. Only ever touches files recorded in the manifest whose content is still exactly as pondhawk wrote it — never a SkipExisting file, never one edited since.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Delete the orphaned files. Omitted or false reports what would be deleted and changes nothing.")]
        bool apply = false)
    {
        var (logger, sw) = ctx.StartToolCall("prune", apply ? "apply=true" : "apply=false");
        var config = ctx.EnsureConfig();

        // Always an unfiltered plan. Pruning against a subset of the templates would treat
        // every file the other templates produce as an orphan.
        var plan = GenerationPlanner.Build(ctx, config, templates: null, items: null, parameters: null, logger);
        var manifest = ManifestStore.Load(ctx.ProjectDir);

        if (!string.IsNullOrEmpty(manifest.OutputDir)
            && !manifest.OutputDir.Equals(plan.ConfiguredOutputDir, StringComparison.Ordinal))
        {
            var message =
                $"OutputDir has changed from '{manifest.OutputDir}' to '{plan.ConfiguredOutputDir}'. Files generated under the "
                + "old directory are outside the new one, so pruning cannot reach them. Move or delete them by hand, "
                + "then run generate to rebuild the manifest.";
            logger.LogWarning("Tool prune refused — {Message}", message);
            return JsonSerializer.Serialize(new { Pruned = 0, Refused = true, Reason = message });
        }

        var produced = plan.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        var removed = new List<object>();
        var kept = new List<object>();

        foreach (var (relativePath, entry) in manifest.Files.ToList())
        {
            if (produced.Contains(relativePath))
                continue;

            // Resolve through the same containment check the writer uses: a manifest is an
            // ordinary file on disk, and nothing that deletes should trust a path from one.
            string fullPath;
            try
            {
                fullPath = FileWriter.ResolveContained(plan.OutputDir, relativePath);
            }
            catch (Exception ex)
            {
                kept.Add(new { RelativePath = relativePath, Reason = "OutsideOutputDir", Detail = ex.Message });
                continue;
            }

            if (!File.Exists(fullPath))
            {
                // Already gone. Drop the entry; there is nothing left to identify.
                if (apply)
                    manifest.Files.Remove(relativePath);
                continue;
            }

            if (entry.Mode.Equals("SkipExisting", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(new
                {
                    RelativePath = relativePath,
                    Reason = "DeveloperFile",
                    Detail = "Written once by a SkipExisting template and owned by the developer since."
                });
                continue;
            }

            if (ManifestStore.HashFile(fullPath) != entry.Hash)
            {
                kept.Add(new
                {
                    RelativePath = relativePath,
                    Reason = "EditedSinceGenerated",
                    Detail = "Content differs from what pondhawk wrote, so deleting it would discard someone's work."
                });
                continue;
            }

            removed.Add(new { RelativePath = relativePath, entry.Template, entry.Node });

            if (apply)
            {
                File.Delete(fullPath);
                manifest.Files.Remove(relativePath);
                RemoveEmptyParents(Path.GetDirectoryName(fullPath), plan.OutputDir);
            }
        }

        if (apply)
            ManifestStore.Save(ctx.ProjectDir, manifest);

        var summary = removed.Count == 0 && kept.Count == 0
            ? "Nothing to prune — every generated file is still produced"
            : string.Join(", ", new[]
            {
                removed.Count > 0 ? (apply ? $"{removed.Count} files deleted" : $"{removed.Count} files would be deleted") : null,
                kept.Count > 0 ? $"{kept.Count} files kept" : null
            }.Where(p => p is not null));

        sw.Stop();
        logger.LogInformation("Tool prune completed in {Duration}ms — {Summary}", sw.ElapsedMilliseconds, summary);

        return JsonSerializer.Serialize(new
        {
            Applied = apply,
            NothingWritten = !apply,
            Pruned = removed.Count,
            Kept = kept.Count,
            Removed = removed,
            KeptFiles = kept,
            OutputDir = plan.OutputDir,
            Summary = summary
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Deleting the last file in a generated folder should take the folder too, but only as far
    /// up as the output directory and only while the folders are genuinely empty.
    /// </summary>
    private static void RemoveEmptyParents(string? directory, string outputDir)
    {
        var root = Path.GetFullPath(outputDir);

        while (!string.IsNullOrEmpty(directory)
               && !PathsEqual(directory, root)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
