using Humanizer;

namespace Pondhawk.Generation.Models;

public sealed class KindSummary
{
    public required string Kind { get; init; }
    public required int Count { get; init; }

    /// <summary>Nesting levels this Kind appears at. 0 is a root node.</summary>
    public required List<int> Depths { get; init; }

    /// <summary>A few real names, so the Kind is recognisable without opening the model.</summary>
    public required List<string> Examples { get; init; }
}

public sealed class MetadataKeySummary
{
    public required string Key { get; init; }

    /// <summary>How many nodes of this Kind carry the key, out of how many there are.</summary>
    public required string Present { get; init; }

    /// <summary>Distinct value types observed. More than one is a concept modelled two ways.</summary>
    public required List<string> Types { get; init; }
}

public sealed class ModelDescription
{
    public required string Model { get; init; }
    public required string Name { get; init; }
    public required int RootNodes { get; init; }
    public required int TotalNodes { get; init; }
    public required int MaxDepth { get; init; }
    public required List<KindSummary> Kinds { get; init; }

    /// <summary>Parent-to-child Kind pairs actually present, e.g. "Class > Property".</summary>
    public required List<string> Structure { get; init; }

    /// <summary>Metadata keys by Kind.</summary>
    public required Dictionary<string, List<MetadataKeySummary>> Metadata { get; init; }

    /// <summary>Inconsistencies worth looking at before adding to this model.</summary>
    public required List<string> Notices { get; init; }
}

/// <summary>
/// Summarises a model's conventions so they can be conformed to without reading it.
/// </summary>
/// <remarks>
/// Extending a model means matching what is already there — the same Kinds, the same metadata
/// keys — because every second convention introduced is a second macro someone has to write,
/// and the set stops being uniform. That obligation is easy to state and, on a large model,
/// expensive to discharge by reading. This reports counts and vocabularies, never node
/// listings, so a five-hundred-node model summarises to roughly the size of a twenty-node one.
/// </remarks>
public static class ModelDescriber
{
    private const int MaxExamples = 3;

    public static ModelDescription Describe(ModelFile model, string modelFile)
    {
        var all = model.Nodes
            .SelectMany(n => n.Descend())
            .Select(d => (d.Node, Depth: d.Path.Count(c => c == '/')))
            .ToList();

        return new ModelDescription
        {
            Model = modelFile,
            Name = model.Name,
            RootNodes = model.Nodes.Count,
            TotalNodes = all.Count,
            MaxDepth = all.Count == 0 ? 0 : all.Max(n => n.Depth),
            Kinds = SummariseKinds(all),
            Structure = Structure(model),
            Metadata = MetadataByKind(all),
            Notices = Notices(all)
        };
    }

    private static List<KindSummary> SummariseKinds(List<(Node Node, int Depth)> all) =>
        all.GroupBy(n => n.Node.Kind, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KindSummary
            {
                Kind = g.Key,
                Count = g.Count(),
                Depths = g.Select(n => n.Depth).Distinct().Order().ToList(),
                Examples = g.Select(n => n.Node.Name).Distinct(StringComparer.Ordinal).Take(MaxExamples).ToList()
            })
            .ToList();

    /// <summary>
    /// The Kind grammar: which Kinds actually nest inside which. It says where a new node is
    /// allowed to go, which a flat list of Kinds does not.
    /// </summary>
    private static List<string> Structure(ModelFile model)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        void Walk(Node node)
        {
            foreach (var child in node.Children)
            {
                pairs.Add($"{node.Kind} > {child.Kind}");
                Walk(child);
            }
        }

        foreach (var root in model.Nodes)
            Walk(root);

        return pairs.Order(StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, List<MetadataKeySummary>> MetadataByKind(List<(Node Node, int Depth)> all)
    {
        var result = new Dictionary<string, List<MetadataKeySummary>>(StringComparer.Ordinal);

        foreach (var group in all.GroupBy(n => n.Node.Kind, StringComparer.Ordinal))
        {
            var nodes = group.Select(n => n.Node).ToList();

            var keys = nodes
                .SelectMany(n => n.Metadata.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(key => new MetadataKeySummary
                {
                    Key = key,
                    Present = $"{nodes.Count(n => n.Metadata.ContainsKey(key))}/{nodes.Count}",
                    Types = nodes
                        .Where(n => n.Metadata.ContainsKey(key))
                        .Select(n => TypeName(n.Metadata[key]))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToList()
                })
                .ToList();

            if (keys.Count > 0)
                result[group.Key] = keys;
        }

        return result;
    }

    private static string TypeName(object? value) => value switch
    {
        null => "null",
        string => "string",
        bool => "boolean",
        long or int or double or float or decimal => "number",
        System.Collections.IDictionary => "object",
        System.Collections.IEnumerable => "list",
        _ => value.GetType().Name.ToLowerInvariant()
    };

    private static List<string> Notices(List<(Node Node, int Depth)> all)
    {
        var notices = new List<string>();
        var kinds = all.Select(n => n.Node.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        NoticeCaseVariantKinds(kinds, notices);
        NoticeSimilarKinds(kinds, notices);

        foreach (var group in all.GroupBy(n => n.Node.Kind, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var nodes = group.Select(n => n.Node).ToList();
            NoticeRivalKeys(group.Key, nodes, notices);
            NoticeMixedTypes(group.Key, nodes, notices);
        }

        return notices;
    }

    /// <summary>
    /// Dispatch builds a macro name from the literal Kind, so Kinds differing only in case
    /// resolve to different macros even though AppliesTo matches both. That is invisible until
    /// one of them renders an error comment into a generated file.
    /// </summary>
    private static void NoticeCaseVariantKinds(List<string> kinds, List<string> notices)
    {
        foreach (var group in kinds.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            notices.Add(
                $"Kinds {Quoted(group)} differ only in case. Dispatch builds macro names from the literal Kind, "
                + $"so they resolve to different macros — Default{group.First()} and Default{group.Last()}.");
        }
    }

    private static void NoticeSimilarKinds(List<string> kinds, List<string> notices)
    {
        for (var i = 0; i < kinds.Count; i++)
        for (var j = i + 1; j < kinds.Count; j++)
        {
            var (a, b) = (kinds[i], kinds[j]);
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
                continue; // already reported as a case variant

            var related = a.Singularize(false).Equals(b.Singularize(false), StringComparison.OrdinalIgnoreCase)
                          || Distance(a, b) <= 2;

            if (related)
                notices.Add($"Kinds '{a}' and '{b}' look like two names for one thing. Each needs its own macro.");
        }
    }

    /// <summary>
    /// A key carried by nearly every node of a Kind, beside a similarly-named one carried by
    /// barely any, is the shape of a second convention creeping in — "DataType" appearing next
    /// to an established "Type".
    /// </summary>
    private static void NoticeRivalKeys(string kind, List<Node> nodes, List<string> notices)
    {
        var coverage = nodes
            .SelectMany(n => n.Metadata.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k, k => nodes.Count(n => n.Metadata.ContainsKey(k)), StringComparer.OrdinalIgnoreCase);

        foreach (var (dominant, dominantCount) in coverage.Where(c => c.Value * 2 >= nodes.Count))
        foreach (var (rare, rareCount) in coverage.Where(c => c.Value * 4 < nodes.Count))
        {
            if (dominant.Equals(rare, StringComparison.OrdinalIgnoreCase) || !Related(dominant, rare))
                continue;

            notices.Add(
                $"{kind}: '{rare}' ({rareCount}/{nodes.Count}) sits beside '{dominant}' ({dominantCount}/{nodes.Count}) "
                + "— likely a second name for one concept.");
        }
    }

    private static void NoticeMixedTypes(string kind, List<Node> nodes, List<string> notices)
    {
        foreach (var key in nodes.SelectMany(n => n.Metadata.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var types = nodes
                .Where(n => n.Metadata.ContainsKey(key))
                .Select(n => TypeName(n.Metadata[key]))
                .Where(t => t != "null")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (types.Count > 1)
                notices.Add($"{kind}: '{key}' holds {string.Join(" and ", types)} values — one concept modelled two ways.");
        }
    }

    private static bool Related(string a, string b)
        => a.Contains(b, StringComparison.OrdinalIgnoreCase)
           || b.Contains(a, StringComparison.OrdinalIgnoreCase)
           || (Math.Min(a.Length, b.Length) >= 4 && Distance(a, b) <= 2);

    /// <summary>Levenshtein distance, for catching a key or Kind that is a typo of another.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(" and ", values.Select(v => $"'{v}'"));
}
