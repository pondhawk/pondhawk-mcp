namespace Pondhawk.Persistence.Core.Models;

/// <summary>
/// A single element of an input model — a class, a property, an endpoint, a parameter.
/// Nodes nest arbitrarily: a class holds properties, an endpoint holds operations which
/// hold parameters. <see cref="Kind"/> names what the node is and drives macro dispatch,
/// so a node of Kind "Property" resolves to the DefaultProperty macro.
///
/// Name, Kind and Children are the whole contract. Everything else an input model carries
/// lives in <see cref="Metadata"/> and is reached from templates as a direct member, so a
/// template writes {{ p.Type }} rather than {{ p.Metadata.Type }}.
/// </summary>
public sealed class Node
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<Node> Children { get; set; } = [];
    public Dictionary<string, object?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> _variants = new(StringComparer.OrdinalIgnoreCase);

    public void SetVariant(string artifactName, string variant) => _variants[artifactName] = variant;

    public string GetVariant(string artifactName)
        => _variants.TryGetValue(artifactName, out var variant) ? variant : "";

    /// <summary>
    /// Resolves a member for template access. The three contract members win over metadata,
    /// so a model that carries a "Name" key cannot shadow the node's own name. Unknown members
    /// return null rather than throwing: metadata is heterogeneous by design, and templates
    /// routinely branch on absence with {% if p.IsKey %}.
    /// </summary>
    public object? GetMember(string name) => name switch
    {
        "Name" => Name,
        "Kind" => Kind,
        "Children" => Children,
        _ => Metadata.TryGetValue(name, out var value) ? value : null
    };

    /// <summary>
    /// Deep copy. Generation clones before applying overrides so that per-artifact variants
    /// and metadata edits never leak across templates or survive into the next generate call.
    /// </summary>
    public Node Clone()
    {
        var clone = new Node
        {
            Name = Name,
            Kind = Kind,
            Metadata = new Dictionary<string, object?>(Metadata, StringComparer.OrdinalIgnoreCase),
            Children = Children.Select(c => c.Clone()).ToList()
        };
        clone._variants = new Dictionary<string, string>(_variants, StringComparer.OrdinalIgnoreCase);
        return clone;
    }

    /// <summary>
    /// This node and every descendant, depth-first, each paired with its slash-delimited
    /// path from the root ("Orders/Submit/CustomerId"). Overrides match against these paths.
    /// </summary>
    public IEnumerable<(Node Node, string Path)> Descend(string prefix = "")
    {
        var path = string.IsNullOrEmpty(prefix) ? Name : $"{prefix}/{Name}";
        yield return (this, path);
        foreach (var child in Children)
            foreach (var descendant in child.Descend(path))
                yield return descendant;
    }
}
