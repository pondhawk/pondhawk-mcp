using System.Text;

namespace Pondhawk.Generation.Rendering;

/// <summary>What writing a given content to a given path would do, or did.</summary>
public enum WriteOutcome
{
    /// <summary>Content was blank; nothing is written.</summary>
    Empty,

    /// <summary>The file exists and the template is SkipExisting; it belongs to the developer now.</summary>
    SkippedExisting,

    /// <summary>No file there yet.</summary>
    Create,

    /// <summary>A file is there and its content differs.</summary>
    Overwrite,

    /// <summary>A file is there and already holds exactly this content.</summary>
    Unchanged
}

public sealed class FileWriteResult
{
    public string Path { get; set; } = "";
    public string Action { get; set; } = ""; // "Created", "Overwritten", "Unchanged", "SkippedExisting", "SkippedEmpty"
}

public static class FileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Writes <paramref name="relativePath"/> beneath <paramref name="rootDir"/>.
    /// The path is resolved and checked for containment first — see <see cref="ResolveContained"/>.
    /// </summary>
    public static FileWriteResult WriteFile(string rootDir, string relativePath, string content, string mode)
        => WriteResolved(ResolveContained(rootDir, relativePath), content, mode);

    /// <summary>
    /// Writes to an already-resolved path. Callers that planned the write earlier — a dry run
    /// deciding what a real run would do — resolve once and reuse the result.
    /// </summary>
    public static FileWriteResult WriteResolved(string fullPath, string content, string mode)
    {
        // Comparing costs a read, and buys not touching a file whose content is already
        // correct. Rewriting it identically would move its timestamp, which is enough to
        // wake every file watcher and incremental build downstream for no reason.
        var outcome = Decide(fullPath, content, mode, compareContent: true);

        if (outcome is not (WriteOutcome.Create or WriteOutcome.Overwrite))
            return new FileWriteResult { Path = fullPath, Action = ActionName(outcome) };

        var dir = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        WriteAtomically(fullPath, content);

        return new FileWriteResult { Path = fullPath, Action = ActionName(outcome) };
    }

    /// <summary>
    /// Writes through a temporary file in the same directory and renames it into place.
    /// </summary>
    /// <remarks>
    /// The rename is atomic, so a crash or a full disk leaves either the previous file or the
    /// new one and never a truncated mixture of the two. Rendering already happens to a string
    /// before any write begins, so this closes the only remaining way a partial file could
    /// reach disk. The temporary must share the destination's directory: renaming across
    /// filesystems is a copy, and a copy is not atomic.
    ///
    /// Deliberately per file rather than per run. A run that fails partway leaves the files it
    /// wrote correctly in place, which is the existing design — rolling those back would mean
    /// deleting correct output. Generation is deterministic and idempotent, so the recovery is
    /// to fix the error and run again, and `check` says what state the tree is in meanwhile.
    /// </remarks>
    private static void WriteAtomically(string fullPath, string content)
    {
        var temp = $"{fullPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temp, content, Utf8NoBom);
            File.Move(temp, fullPath, overwrite: true);
        }
        catch
        {
            // A failed write must not leave its scratch file behind in the output tree.
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // Nothing useful to do; the original failure is what matters.
            }

            throw;
        }
    }

    /// <summary>
    /// Decides what writing this content to this path would do. The write path and the dry run
    /// both go through here, so a preview cannot disagree with the run it is previewing.
    /// </summary>
    /// <param name="compareContent">
    /// When true, an existing file holding identical content reports
    /// <see cref="WriteOutcome.Unchanged"/> instead of <see cref="WriteOutcome.Overwrite"/>.
    /// Only a caller that is not going to write needs that distinction, and it costs a read.
    /// </param>
    public static WriteOutcome Decide(string fullPath, string content, string mode, bool compareContent)
    {
        if (string.IsNullOrWhiteSpace(content))
            return WriteOutcome.Empty;

        var exists = File.Exists(fullPath);

        if (exists && mode.Equals("SkipExisting", StringComparison.OrdinalIgnoreCase))
            return WriteOutcome.SkippedExisting;

        if (!exists)
            return WriteOutcome.Create;

        if (compareContent && File.ReadAllText(fullPath) == content)
            return WriteOutcome.Unchanged;

        return WriteOutcome.Overwrite;
    }

    private static string ActionName(WriteOutcome outcome) => outcome switch
    {
        WriteOutcome.Empty => "SkippedEmpty",
        WriteOutcome.SkippedExisting => "SkippedExisting",
        WriteOutcome.Create => "Created",
        WriteOutcome.Unchanged => "Unchanged",
        _ => "Overwritten"
    };

    /// <summary>
    /// Resolves an output path beneath a root directory, refusing anything that escapes it.
    ///
    /// Output paths are rendered from node names, so they are only as trustworthy as the input
    /// model. A name of "../.." walks out of the output directory, and a name beginning with a
    /// separator makes the path absolute and discards the root entirely — neither is
    /// necessarily malicious. A model derived from API routes or namespaced identifiers
    /// produces both by accident.
    /// </summary>
    public static string ResolveContained(string rootDir, string relativePath)
    {
        var root = System.IO.Path.GetFullPath(rootDir);
        var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativePath));

        var relative = System.IO.Path.GetRelativePath(root, combined);
        var escapes = relative == ".."
                      || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
                      || System.IO.Path.IsPathRooted(relative);

        if (escapes)
        {
            throw new InvalidOperationException(
                $"Refusing to write '{relativePath}': it resolves to '{combined}', outside the output directory " +
                $"'{root}'. Output paths come from node names — check the name for leading separators or '..'.");
        }

        return combined;
    }
}
