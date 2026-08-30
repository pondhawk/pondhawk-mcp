using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pondhawk.Generation.Manifest;

/// <summary>Provenance for one generated file.</summary>
public sealed class ManifestEntry
{
    /// <summary>Template key that produced it.</summary>
    public string Template { get; set; } = "";

    /// <summary>Node the file came from, or the template key for a Single-scope render.</summary>
    public string Node { get; set; } = "";

    /// <summary>Model file the node came from.</summary>
    public string Model { get; set; } = "";

    /// <summary>Always or SkipExisting. A SkipExisting file belongs to the developer once it exists.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Hash of the content pondhawk last wrote — not of whatever is on disk now.</summary>
    public string Hash { get; set; } = "";
}

/// <summary>
/// What pondhawk believes it has written into the output directory.
/// </summary>
/// <remarks>
/// A snapshot of the current state of the output tree, not a log of runs. Every question asked
/// of it is about now — is this file still produced, was it edited since it was generated — and
/// git already versions the file for anyone who wants the history.
///
/// It accumulates rather than being replaced, because a filtered run produces only some of the
/// tree and must not orphan the rest. For the same reason `generate` only ever adds and updates:
/// an entry for a file that is no longer produced is exactly the evidence that identifies it as
/// an orphan, so dropping it would destroy the proof needed to remove it safely. Only `prune`
/// removes entries.
///
/// It carries no timestamps or run counters, so regenerating an unchanged project leaves the
/// file byte-identical and a committed manifest stays quiet in `git status`.
/// </remarks>
public sealed class GenerationManifest
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// The output directory these paths are relative to. If the configured one changes,
    /// previously generated files are stranded outside the new root rather than orphaned
    /// inside it, and that is worth saying out loud rather than silently ignoring.
    /// </summary>
    public string OutputDir { get; set; } = "";

    /// <summary>Keyed by path relative to <see cref="OutputDir"/>, ordered for a stable file.</summary>
    public SortedDictionary<string, ManifestEntry> Files { get; set; } = new(StringComparer.Ordinal);
}

[JsonSerializable(typeof(GenerationManifest))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
public partial class GenerationManifestContext : JsonSerializerContext;

public static class ManifestStore
{
    public const string RelativePath = ".pondhawk/manifest.json";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string PathFor(string projectDir) => Path.Combine(projectDir, ".pondhawk", "manifest.json");

    /// <summary>Loads the manifest, or an empty one when a project has never generated.</summary>
    public static GenerationManifest Load(string projectDir)
    {
        var path = PathFor(projectDir);
        if (!File.Exists(path))
            return new GenerationManifest();

        try
        {
            var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), GenerationManifestContext.Default.GenerationManifest);
            return manifest ?? new GenerationManifest();
        }
        catch (JsonException)
        {
            // A corrupt manifest must not stop generation. The cost of starting over is that
            // files written before now are no longer identifiable as orphans; the cost of
            // throwing is that the project cannot generate at all.
            return new GenerationManifest();
        }
    }

    /// <summary>
    /// Writes the manifest through a temporary file, so an interrupted run leaves the previous
    /// one intact rather than a half-written one.
    /// </summary>
    public static void Save(string projectDir, GenerationManifest manifest)
    {
        var path = PathFor(projectDir);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        EnsureLogsIgnored(projectDir);

        var json = JsonSerializer.Serialize(manifest, GenerationManifestContext.Default.GenerationManifest);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, json, Utf8NoBom);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// The manifest is meant to be committed and the log directory beside it is not, so the
    /// folder carries the rule that separates them.
    /// </summary>
    public static void EnsureLogsIgnored(string projectDir)
    {
        var pondhawkDir = Path.Combine(projectDir, ".pondhawk");
        Directory.CreateDirectory(pondhawkDir);

        var gitignore = Path.Combine(pondhawkDir, ".gitignore");
        if (File.Exists(gitignore))
            return;

        File.WriteAllText(gitignore, """
            # manifest.json records what pondhawk generated and belongs in version control.
            # Logs do not.
            logs/
            *.tmp

            """, Utf8NoBom);
    }

    public static string HashContent(string content)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Utf8NoBom.GetBytes(content)));

    /// <summary>Hash of a file as it currently sits on disk, or null when it is gone.</summary>
    public static string? HashFile(string fullPath)
        => File.Exists(fullPath) ? HashContent(File.ReadAllText(fullPath)) : null;
}
