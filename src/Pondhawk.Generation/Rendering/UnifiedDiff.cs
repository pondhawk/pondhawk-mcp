using System.Text;

namespace Pondhawk.Generation.Rendering;

/// <summary>
/// Line diffs in unified format, for showing what a generation run would change before it
/// changes it.
/// </summary>
public static class UnifiedDiff
{
    /// <summary>
    /// Beyond this many differing lines on either side, an exact diff costs more to compute and
    /// to read than it is worth, and the honest answer is that the file was rewritten.
    /// </summary>
    private const int ExactDiffLimit = 1000;

    /// <summary>
    /// Produces a unified diff of <paramref name="before"/> against <paramref name="after"/>,
    /// or an empty string when they are identical.
    /// </summary>
    /// <param name="path">Path shown in the ---/+++ header.</param>
    /// <param name="context">Unchanged lines kept either side of each change.</param>
    /// <param name="maxLines">
    /// Cap on emitted body lines. A generation run can touch hundreds of files, and an
    /// uncapped diff of all of them buries the summary it was meant to support.
    /// </param>
    public static string Create(string before, string after, string path, int context = 3, int maxLines = 400)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return "";

        var oldLines = SplitLines(before);
        var newLines = SplitLines(after);

        // Generated files usually change in one place, so trimming the shared head and tail
        // reduces the interesting middle to something small enough to diff exactly.
        var prefix = CommonPrefix(oldLines, newLines);
        var suffix = CommonSuffix(oldLines, newLines, prefix);

        var oldMiddle = oldLines[prefix..(oldLines.Length - suffix)];
        var newMiddle = newLines[prefix..(newLines.Length - suffix)];

        var body = oldMiddle.Length > ExactDiffLimit || newMiddle.Length > ExactDiffLimit
            ? RewriteHunk(oldMiddle, newMiddle, prefix)
            : ExactHunk(oldMiddle, newMiddle, prefix, suffix, oldLines, context);

        var sb = new StringBuilder();
        sb.Append("--- ").Append(path).Append(" (on disk)\n");
        sb.Append("+++ ").Append(path).Append(" (would generate)\n");

        var emitted = 0;
        foreach (var line in body)
        {
            if (emitted++ == maxLines)
            {
                sb.Append("… diff truncated at ").Append(maxLines).Append(" lines\n");
                break;
            }

            sb.Append(line).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>True when the two texts differ at all.</summary>
    public static bool Differs(string before, string after)
        => !string.Equals(before, after, StringComparison.Ordinal);

    private static List<string> ExactHunk(
        string[] oldMiddle, string[] newMiddle, int prefix, int suffix, string[] oldLines, int context)
    {
        // Re-anchor the middle's edit script onto the whole file so hunk headers carry real
        // line numbers rather than offsets into the trimmed region.
        var anchored = new List<(char Op, string Text)>();

        for (var i = 0; i < prefix; i++)
            anchored.Add((' ', oldLines[i]));

        anchored.AddRange(Lcs(oldMiddle, newMiddle));

        for (var i = oldLines.Length - suffix; i < oldLines.Length; i++)
            anchored.Add((' ', oldLines[i]));

        return Hunks(anchored, context);
    }

    private static List<string> RewriteHunk(string[] oldMiddle, string[] newMiddle, int prefix)
    {
        var lines = new List<string>
        {
            $"@@ -{prefix + 1},{oldMiddle.Length} +{prefix + 1},{newMiddle.Length} @@ (rewritten)"
        };
        lines.AddRange(oldMiddle.Select(l => "-" + l));
        lines.AddRange(newMiddle.Select(l => "+" + l));
        return lines;
    }

    /// <summary>Longest-common-subsequence edit script over the differing middle.</summary>
    private static List<(char Op, string Text)> Lcs(string[] a, string[] b)
    {
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lengths[i, j] = a[i] == b[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var script = new List<(char, string)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                script.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                script.Add(('-', a[x]));
                x++;
            }
            else
            {
                script.Add(('+', b[y]));
                y++;
            }
        }

        while (x < a.Length) script.Add(('-', a[x++]));
        while (y < b.Length) script.Add(('+', b[y++]));
        return script;
    }

    /// <summary>Groups an edit script into hunks, keeping <paramref name="context"/> lines around each change.</summary>
    private static List<string> Hunks(List<(char Op, string Text)> script, int context)
    {
        var output = new List<string>();
        var changed = script.Select((e, i) => (e.Op, i)).Where(t => t.Op != ' ').Select(t => t.i).ToList();
        if (changed.Count == 0)
            return output;

        var index = 0;
        while (index < changed.Count)
        {
            var start = Math.Max(0, changed[index] - context);
            var end = changed[index];

            // Absorb following changes whose context windows touch this one.
            while (index + 1 < changed.Count && changed[index + 1] - end <= context * 2)
            {
                index++;
                end = changed[index];
            }

            end = Math.Min(script.Count - 1, end + context);
            index++;

            int oldStart = 1, newStart = 1;
            for (var i = 0; i < start; i++)
            {
                if (script[i].Op != '+') oldStart++;
                if (script[i].Op != '-') newStart++;
            }

            var oldCount = 0;
            var newCount = 0;
            for (var i = start; i <= end; i++)
            {
                if (script[i].Op != '+') oldCount++;
                if (script[i].Op != '-') newCount++;
            }

            output.Add($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");
            for (var i = start; i <= end; i++)
                output.Add(script[i].Op + script[i].Text);
        }

        return output;
    }

    private static int CommonPrefix(string[] a, string[] b)
    {
        var limit = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }

    private static int CommonSuffix(string[] a, string[] b, int prefix)
    {
        var limit = Math.Min(a.Length, b.Length) - prefix;
        var i = 0;
        while (i < limit && a[a.Length - 1 - i] == b[b.Length - 1 - i]) i++;
        return i;
    }

    /// <summary>
    /// Splits on newlines without normalizing them. A run that changes only line endings is a
    /// real change to the file, so a carriage return stays part of its line — normalizing here
    /// would report the file as differing and then show a diff with nothing in it.
    /// </summary>
    private static string[] SplitLines(string text) =>
        text.Length == 0 ? [] : text.Split('\n');
}
