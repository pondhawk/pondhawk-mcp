using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pondhawk.Persistence.Core.Configuration;

public sealed class ProjectConfiguration
{
    [JsonPropertyName("$schema")]
    public string? Schema_ { get; set; }

    public string? ProjectName { get; set; }
    public string? Description { get; set; }

    public ConnectionConfig Connection { get; set; } = new();
    public string OutputDir { get; set; } = "";
    public Dictionary<string, TemplateConfig> Templates { get; set; } = new();
    public DefaultsConfig Defaults { get; set; } = new();
    public Dictionary<string, DataTypeConfig> DataTypes { get; set; } = new();
    public List<TypeMappingConfig> TypeMappings { get; set; } = [];
    public List<RelationshipConfig> Relationships { get; set; } = [];
    public List<OverrideConfig> Overrides { get; set; } = [];
    public LoggingConfig Logging { get; set; } = new();
}

public sealed class ConnectionConfig
{
    public string Provider { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

public sealed class TemplateConfig
{
    public string Path { get; set; } = "";
    public string OutputPattern { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Mode { get; set; } = "";
    public string? AppliesTo { get; set; }
}

public sealed class DefaultsConfig
{
    public string? Namespace { get; set; }
    public string? ContextName { get; set; }
    public string Schema { get; set; } = "dbo";
    public bool IncludeViews { get; set; }
    public List<string>? Include { get; set; }
    public List<string>? Exclude { get; set; }
}

public sealed class DataTypeConfig
{
    public string ClrType { get; set; } = "";
    public int? MaxLength { get; set; }
    public string? DefaultValue { get; set; }
}

public sealed class TypeMappingConfig
{
    public string DbType { get; set; } = "";
    public string? DataType { get; set; }
    public string? ClrType { get; set; }
}

public sealed class RelationshipConfig
{
    public string DependentTable { get; set; } = "";
    public string? DependentSchema { get; set; }
    public List<string> DependentColumns { get; set; } = [];
    public string PrincipalTable { get; set; } = "";
    public string? PrincipalSchema { get; set; }
    public List<string> PrincipalColumns { get; set; } = [];
    public string OnDelete { get; set; } = "NoAction";
}

public sealed class OverrideConfig
{
    public string Class { get; set; } = "";
    public string? Property { get; set; }
    public string? Artifact { get; set; }
    public string? Variant { get; set; }
    public string? DataType { get; set; }
    public bool Ignore { get; set; }
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
    public static ProjectConfiguration Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return Deserialize(json);
    }

    public static ProjectConfiguration Deserialize(string json)
    {
        return JsonSerializer.Deserialize(json, ProjectConfigurationContext.Default.ProjectConfiguration)
               ?? throw new JsonException("Failed to deserialize configuration: result was null");
    }

    public static string Serialize(ProjectConfiguration config)
    {
        return JsonSerializer.Serialize(config, ProjectConfigurationContext.Default.ProjectConfiguration);
    }

    public static void Save(string filePath, ProjectConfiguration config)
    {
        var json = Serialize(config);
        File.WriteAllText(filePath, json, new System.Text.UTF8Encoding(false));
    }
}
