using Pondhawk.Generation.Rendering;
using Shouldly;

namespace Pondhawk.Generation.Tests.Rendering;

public class UnifiedDiffTests
{
    private const string Original = """
        class Product
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
        }
        """;

    [Fact]
    public void IdenticalText_ProducesNoDiff()
    {
        UnifiedDiff.Create(Original, Original, "Product.cs").ShouldBeEmpty();
        UnifiedDiff.Differs(Original, Original).ShouldBeFalse();
    }

    [Fact]
    public void ChangedLine_ShowsBothSidesWithContext()
    {
        var after = Original.Replace("decimal Price", "decimal? Price");

        var diff = UnifiedDiff.Create(Original, after, "Product.cs");

        diff.ShouldContain("--- Product.cs (on disk)");
        diff.ShouldContain("+++ Product.cs (would generate)");
        diff.ShouldContain("-    public decimal Price { get; set; }");
        diff.ShouldContain("+    public decimal? Price { get; set; }");
        diff.ShouldContain("     public string Name { get; set; }");
    }

    [Fact]
    public void HunkHeader_CountsFromTheStartOfTheWholeFile()
    {
        // The middle is diffed after trimming the shared head, so the anchoring has to put the
        // line numbers back or every hunk header is wrong.
        var after = Original.Replace("decimal Price", "decimal? Price");

        var header = UnifiedDiff.Create(Original, after, "Product.cs")
            .Split('\n')
            .First(l => l.StartsWith("@@"));

        // The change is on line 5 of 6; three lines of context put the hunk's first line at 2,
        // and it runs to the end of the file — five lines on both sides.
        header.ShouldBe("@@ -2,5 +2,5 @@");
    }

    [Fact]
    public void AddedLine_IsMarkedAsAnAddition()
    {
        var after = Original.Replace(
            "    public decimal Price { get; set; }",
            "    public decimal Price { get; set; }\n    public bool InStock { get; set; }");

        var diff = UnifiedDiff.Create(Original, after, "Product.cs");

        diff.ShouldContain("+    public bool InStock { get; set; }");
        diff.ShouldNotContain("-    public decimal Price { get; set; }");
    }

    [Fact]
    public void RemovedLine_IsMarkedAsARemoval()
    {
        var after = Original.Replace("    public string Name { get; set; }\n", "");

        var diff = UnifiedDiff.Create(Original, after, "Product.cs");

        diff.ShouldContain("-    public string Name { get; set; }");
    }

    [Fact]
    public void NewFileAgainstNothing_IsAllAdditions()
    {
        var diff = UnifiedDiff.Create("", Original, "Product.cs");

        diff.ShouldContain("+class Product");
        diff.ShouldNotContain("\n-");
    }

    [Fact]
    public void LineEndingOnlyChange_IsReportedAndShown()
    {
        // Reported as differing but shown as an empty diff would be the worst outcome: the run
        // says something changed and cannot say what.
        var crlf = Original.Replace("\n", "\r\n");

        UnifiedDiff.Differs(Original, crlf).ShouldBeTrue();

        var diff = UnifiedDiff.Create(Original, crlf, "Product.cs");

        diff.ShouldContain("@@");
        diff.Split('\n').ShouldContain(l => l.StartsWith('-'), "the diff body must show the change it reported");
    }

    [Fact]
    public void DistantChanges_BecomeSeparateHunks()
    {
        var before = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"line {i}"));
        var after = before.Replace("line 2\n", "CHANGED 2\n").Replace("line 25", "CHANGED 25");

        var hunks = UnifiedDiff.Create(before, after, "Product.cs")
            .Split('\n')
            .Count(l => l.StartsWith("@@"));

        hunks.ShouldBe(2);
    }

    [Fact]
    public void NearbyChanges_ShareOneHunk()
    {
        var after = Original
            .Replace("int Id", "long Id")
            .Replace("string Name", "string? Name");

        var hunks = UnifiedDiff.Create(Original, after, "Product.cs")
            .Split('\n')
            .Count(l => l.StartsWith("@@"));

        hunks.ShouldBe(1);
    }

    [Fact]
    public void LongDiff_IsTruncatedRatherThanBuryingTheSummary()
    {
        var before = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"line {i}"));
        var after = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"changed {i}"));

        var diff = UnifiedDiff.Create(before, after, "Big.cs", maxLines: 20);

        diff.ShouldContain("diff truncated at 20 lines");
        diff.Split('\n').Length.ShouldBeLessThan(30);
    }

    [Fact]
    public void VeryLargeChange_FallsBackToAWholesaleRewrite()
    {
        // Past the exact-diff limit an LCS matrix costs more than the answer is worth, and
        // "this file was rewritten" is the truthful summary.
        var before = string.Join("\n", Enumerable.Range(0, 1500).Select(i => $"old {i}"));
        var after = string.Join("\n", Enumerable.Range(0, 1500).Select(i => $"new {i}"));

        var diff = UnifiedDiff.Create(before, after, "Huge.cs", maxLines: 10_000);

        diff.ShouldContain("(rewritten)");
    }
}
