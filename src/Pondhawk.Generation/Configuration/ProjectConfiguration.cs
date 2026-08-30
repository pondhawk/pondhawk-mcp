using System.Text.Json;
using System.Text.Json.Serialization;
using Pondhawk.Generation.Json;

namespace Pondhawk.Generation.Configuration;

public sealed class ProjectConfiguration
{
    [JsonPropertyName("$schema")]
    public string? Schema_ { get; set; }

    public string? ProjectName { get; set; }
    public string? Description { get; set; }

    public string OutputDir { get; set; } = "";
    public Dictionary<string, TemplateConfig> Templates { get; set; } = new();

    /// <summary>
    /// Project-wide values handed to every template as {{ values.X }} — a namespace, a package
    /// name, a copyright line. Open-ended by design: what belongs here depends entirely on what
    /// the templates generate.
    /// </summary>
    [JsonConverter(typeof(PlainDictionaryConverter))]
    public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<OverrideConfig> Overrides { get; set; } = [];
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class TemplateConfig
{
    public string Path { get; set; } = "";
    public string OutputPattern { get; set; } = "";

    /// <summary>"PerItem" renders once per matching node; "Single" renders one file for all of them.</summary>
    public string Scope { get; set; } = "";

    /// <summary>"Always" overwrites on every run; "SkipExisting" writes once and then leaves the file alone.</summary>
    public string Mode { get; set; } = "";

    /// <summary>
    /// Restricts this template to nodes of one Kind. Empty or "All" matches every top-level node.
    /// </summary>
    public string? AppliesTo { get; set; }

    /// <summary>
    /// The model file this template renders, relative to the project. Empty means "model.json".
    /// A project with two unrelated generation concerns — say entities edited by hand and an API
    /// surface regenerated from a spec — keeps them in separate models rather than splicing both
    /// into one document, and each template says which one it reads.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// The model file this template reads, with the default applied. Derived from
    /// <see cref="Model"/>, so it must never be written back into the config — the schema
    /// forbids unknown properties and a round-trip through init or update would fail.
    /// </summary>
    [JsonIgnore]
    public string ModelFile => string.IsNullOrWhiteSpace(Model) ? DefaultModelFile : Model;

    public const string DefaultModelFile = "model.json";
}

/// <summary>
/// A rule that changes how matched nodes render for one artifact — selecting a variant macro,
/// merging extra metadata, or dropping the node entirely.
/// </summary>
public sealed class OverrideConfig
{
    /// <summary>
    /// Slash-delimited node path. '*' matches one node, '**' matches any depth:
    /// "Products/Price", "*/CreatedAt", "Orders/**".
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>Template key this rule applies to. Empty applies it to every template.</summary>
    public string? Artifact { get; set; }

    /// <summary>Macro variant to render matched nodes with, e.g. "Currency" for CurrencyProperty.</summary>
    public string? Variant { get; set; }

    /// <summary>Drops matched nodes from this artifact.</summary>
    public bool Ignore { get; set; }

    /// <summary>Metadata merged onto matched nodes, overwriting keys the model itself supplied.</summary>
    [JsonConverter(typeof(PlainDictionaryConverter))]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class LoggingConfig
{
    public bool Enabled { get; set; }
    public string LogPath { get; set; } = ".pondhawk/logs/pondhawk.log";
    public string Level { get; set; } = "Debug";
    public string RollingInterval { get; set; } = "Day";
    public int RetainedFileCountLimit { get; set; } = 7;
}

[JsonSerializable(typeof(ProjectConfiguration))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
public partial class ProjectConfigurationContext : JsonSerializerContext;

public static class ProjectConfigurationLoader
{
    public static ProjectConfiguration Load(string filePath) => Deserialize(File.ReadAllText(filePath));

    public static ProjectConfiguration Deserialize(string json)
        => JsonSerializer.Deserialize(json, ProjectConfigurationContext.Default.ProjectConfiguration)
           ?? throw new JsonException("Failed to deserialize configuration: result was null");

    public static string Serialize(ProjectConfiguration config)
        => JsonSerializer.Serialize(config, ProjectConfigurationContext.Default.ProjectConfiguration);

    public static void Save(string filePath, ProjectConfiguration config)
        => File.WriteAllText(filePath, Serialize(config), new System.Text.UTF8Encoding(false));
}
