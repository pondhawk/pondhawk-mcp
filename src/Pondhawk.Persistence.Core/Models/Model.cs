namespace Pondhawk.Persistence.Core.Models;

public sealed class ForeignKey
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public string PrincipalTable { get; set; } = "";
    public string PrincipalSchema { get; set; } = "";
    public List<string> PrincipalColumns { get; set; } = [];
    public string OnDelete { get; set; } = "NoAction";
    public string? OnUpdate { get; set; }
}

public sealed class ReferencingForeignKey
{
    public string Name { get; set; } = "";
    public string Table { get; set; } = "";
    public string Schema { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public List<string> PrincipalColumns { get; set; } = [];
}

public sealed class PrimaryKeyInfo
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
}

public sealed class IndexInfo
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public bool IsUnique { get; set; }
}

public sealed class Model
{
    public string Name { get; set; } = "";
    public string Schema { get; set; } = "";
    public bool IsView { get; set; }
    public string? Note { get; set; }
    public List<Attribute> Attributes { get; set; } = [];
    public PrimaryKeyInfo? PrimaryKey { get; set; }
    public List<ForeignKey> ForeignKeys { get; set; } = [];
    public List<ReferencingForeignKey> ReferencingForeignKeys { get; set; } = [];
    public List<IndexInfo> Indexes { get; set; } = [];

    private Dictionary<string, string> _variants = new(StringComparer.OrdinalIgnoreCase);

    public void SetVariant(string artifactName, string variant)
    {
        _variants[artifactName] = variant;
    }

    public string GetVariant(string artifactName)
    {
        return _variants.TryGetValue(artifactName, out var variant) ? variant : "";
    }
}
