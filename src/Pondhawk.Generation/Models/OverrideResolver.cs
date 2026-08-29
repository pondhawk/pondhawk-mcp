using Pondhawk.Generation.Configuration;

namespace Pondhawk.Generation.Models;

/// <summary>
/// Applies per-artifact overrides to a model tree: selects the variant macro a node renders
/// with, merges extra metadata onto it, and drops nodes an artifact should not emit.
///
/// Overrides address nodes by slash-delimited path ("Orders/Submit/CustomerId"). A '*' segment
/// matches any single node, '**' matches any run of nodes at any depth. When several overrides
/// match one node, the one with the most literal segments wins; ties go to the later rule,
/// so a general rule can be stated first and narrowed afterwards.
/// </summary>
public static class OverrideResolver
{
    /// <summary>
    /// Applies overrides to <paramref name="nodes"/> for one artifact, returning the tree with
    /// ignored nodes removed. Mutates the nodes it is given — callers pass clones so that
    /// per-artifact results never leak between templates.
    /// </summary>
    public static List<Node> Apply(List<Node> nodes, string artifactName, List<OverrideConfig> overrides)
    {
        var applicable = overrides
            .Where(o => string.IsNullOrEmpty(o.Artifact)
                     || string.Equals(o.Artifact, artifactName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return applicable.Count == 0 ? nodes : ApplyTo(nodes, "", artifactName, applicable);
    }

    private static List<Node> ApplyTo(List<Node> nodes, string prefix, string artifactName, List<OverrideConfig> overrides)
    {
        var kept = new List<Node>();

        foreach (var node in nodes)
        {
            var path = string.IsNullOrEmpty(prefix) ? node.Name : $"{prefix}/{node.Name}";
            var matches = Matching(overrides, path);

            if (MostSpecific(matches, o => o.Ignore ? "ignore" : null) is not null)
                continue;

            var variant = MostSpecific(matches, o => string.IsNullOrEmpty(o.Variant) ? null : o.Variant);
            if (variant is not null)
                node.SetVariant(artifactName, variant);

            // Least specific first, so a narrower rule's keys land on top of a broader one's.
            foreach (var (ovr, _) in matches.Where(m => m.Override.Metadata is { Count: > 0 })
                                            .OrderBy(m => Specificity(m.Override.Path))
                                            .ThenBy(m => m.Index))
                foreach (var (key, value) in ovr.Metadata!)
                    node.Metadata[key] = value;

            node.Children = ApplyTo(node.Children, path, artifactName, overrides);
            kept.Add(node);
        }

        return kept;
    }

    private static List<(OverrideConfig Override, int Index)> Matching(List<OverrideConfig> overrides, string path)
    {
        var segments = path.Split('/');
        var matches = new List<(OverrideConfig, int)>();
        for (var i = 0; i < overrides.Count; i++)
            if (MatchesPath(overrides[i].Path, segments))
                matches.Add((overrides[i], i));
        return matches;
    }

    /// <summary>Picks the value from the most specific matching override; null when none match.</summary>
    private static string? MostSpecific(
        List<(OverrideConfig Override, int Index)> matches,
        Func<OverrideConfig, string?> select)
    {
        string? result = null;
        var bestSpecificity = -1;
        var bestIndex = -1;

        foreach (var (ovr, index) in matches)
        {
            var value = select(ovr);
            if (value is null) continue;

            var specificity = Specificity(ovr.Path);
            if (specificity > bestSpecificity || (specificity == bestSpecificity && index > bestIndex))
            {
                result = value;
                bestSpecificity = specificity;
                bestIndex = index;
            }
        }

        return result;
    }

    /// <summary>Literal segments in a pattern. More literals means a narrower rule.</summary>
    private static int Specificity(string pattern)
        => string.IsNullOrEmpty(pattern)
            ? 0
            : pattern.Split('/').Count(s => s != "*" && s != "**");

    public static bool MatchesPath(string pattern, string path)
        => !string.IsNullOrEmpty(pattern) && MatchesPath(pattern, path.Split('/'));

    private static bool MatchesPath(string pattern, string[] path)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        return Match(pattern.Split('/'), 0, path, 0);
    }

    private static bool Match(string[] pattern, int p, string[] path, int s)
    {
        while (true)
        {
            if (p == pattern.Length) return s == path.Length;

            if (pattern[p] == "**")
            {
                // Absorb zero or more segments; try the shortest match first.
                for (var skip = s; skip <= path.Length; skip++)
                    if (Match(pattern, p + 1, path, skip))
                        return true;
                return false;
            }

            if (s == path.Length) return false;
            if (pattern[p] != "*" && !string.Equals(pattern[p], path[s], StringComparison.OrdinalIgnoreCase))
                return false;

            p++;
            s++;
        }
    }
}
