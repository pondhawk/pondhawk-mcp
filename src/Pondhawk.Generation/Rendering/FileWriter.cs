using System.Text;

namespace Pondhawk.Generation.Rendering;

public sealed class FileWriteResult
{
    public string Path { get; set; } = "";
    public string Action { get; set; } = ""; // "Created", "Overwritten", "SkippedExisting", "SkippedEmpty"
}

public static class FileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Writes <paramref name="relativePath"/> beneath <paramref name="rootDir"/>.
    /// The path is resolved and checked for containment first — see <see cref="ResolveContained"/>.
    /// </summary>
    public static FileWriteResult WriteFile(string rootDir, string relativePath, string content, string mode)
    {
        var fullPath = ResolveContained(rootDir, relativePath);

        if (string.IsNullOrWhiteSpace(content))
        {
            return new FileWriteResult { Path = fullPath, Action = "SkippedEmpty" };
        }

        var exists = File.Exists(fullPath);

        if (mode.Equals("SkipExisting", StringComparison.OrdinalIgnoreCase) && exists)
        {
            return new FileWriteResult { Path = fullPath, Action = "SkippedExisting" };
        }

        var dir = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content, Utf8NoBom);

        return new FileWriteResult
        {
            Path = fullPath,
            Action = exists ? "Overwritten" : "Created"
        };
    }

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
