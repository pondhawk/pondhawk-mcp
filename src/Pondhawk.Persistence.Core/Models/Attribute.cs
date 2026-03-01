namespace Pondhawk.Persistence.Core.Models;

public sealed class Attribute
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string ClrType { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public string? DefaultValue { get; set; }
    public string? Note { get; set; }

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
