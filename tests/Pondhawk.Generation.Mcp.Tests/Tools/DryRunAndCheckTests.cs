using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

public class DryRunAndCheckTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public DryRunAndCheckTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_dryrun_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        _outputDir = Path.Combine(_tempDir, "output");

        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product",  "Kind": "Class", "Type": "int" },
                { "Name": "Category", "Kind": "Class", "Type": "int" }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private ServerContext Configure(string template = "class {{ item.Name }} {}", string mode = "Always")
    {
        File.WriteAllText(Path.Combine(_tempDir, "templates", "entity.liquid"), template);

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = _outputDir,
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = "{{ item.Name }}.cs",
                    Scope = "PerItem",
                    Mode = mode
                }
            }
        });

        return new ServerContext(_tempDir);
    }

    private static JsonElement Json(string result) => JsonDocument.Parse(result).RootElement;

    private static List<JsonElement> Files(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().ToList();

    // --- dry run writes nothing ---------------------------------------------

    [Fact]
    public void DryRun_WritesNothing()
    {
        var result = Json(GenerateTool.Execute(Configure(), dryRun: true));

        result.GetProperty("DryRun").GetBoolean().ShouldBeTrue();
        result.GetProperty("WouldCreate").GetInt32().ShouldBe(2);
        Directory.Exists(_outputDir).ShouldBeFalse("a dry run must not create the output directory");
    }

    [Fact]
    public void DryRun_OnAnUnchangedTree_ReportsEverythingCurrent()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        var result = Json(GenerateTool.Execute(ctx, dryRun: true));

        result.GetProperty("Unchanged").GetInt32().ShouldBe(2);
        result.GetProperty("WouldOverwrite").GetInt32().ShouldBe(0);
        result.GetProperty("Summary").GetString().ShouldContain("already current");
    }

    [Fact]
    public void DryRun_AfterATemplateChange_ShowsTheDiff()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        // The case the feature exists for: change one macro, see the blast radius first.
        var changed = Configure("sealed class {{ item.Name }} {}");
        var result = Json(GenerateTool.Execute(changed, dryRun: true));

        result.GetProperty("WouldOverwrite").GetInt32().ShouldBe(2);

        var diff = Files(result, "FilesPlanned")
            .First(f => f.GetProperty("RelativePath").GetString() == "Product.cs")
            .GetProperty("Diff").GetString()!;

        diff.ShouldContain("-class Product {}");
        diff.ShouldContain("+sealed class Product {}");
    }

    [Fact]
    public void DryRun_DoesNotDisturbTheFilesItReportsOn()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);
        var before = File.ReadAllText(Path.Combine(_outputDir, "Product.cs"));

        GenerateTool.Execute(Configure("sealed class {{ item.Name }} {}"), dryRun: true);

        File.ReadAllText(Path.Combine(_outputDir, "Product.cs")).ShouldBe(before);
    }

    [Fact]
    public void DryRun_ReportsSkipExistingRatherThanClaimingAChange()
    {
        var ctx = Configure(mode: "SkipExisting");
        GenerateTool.Execute(ctx);

        var result = Json(GenerateTool.Execute(Configure("different {{ item.Name }}", "SkipExisting"), dryRun: true));

        result.GetProperty("WouldSkip").GetInt32().ShouldBe(2);
        result.GetProperty("WouldOverwrite").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void DryRun_SurfacesTheEmptyRenderThatARealRunSkipsSilently()
    {
        var result = Json(GenerateTool.Execute(Configure("{% if false %}x{% endif %}"), dryRun: true));

        result.GetProperty("WouldSkip").GetInt32().ShouldBe(2);
        Files(result, "FilesPlanned")
            .ShouldAllBe(f => f.GetProperty("Action").GetString() == "WouldSkipEmpty");
    }

    [Fact]
    public void DryRun_RefusesAnEscapingPathExactlyAsARealRunWould()
    {
        // A preview that shows a write the real run would reject is worse than no preview.
        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            { "Name": "X", "Nodes": [ { "Name": "../escape", "Kind": "Class" } ] }
            """);

        var result = Json(GenerateTool.Execute(Configure(), dryRun: true));

        result.GetProperty("Failed").GetInt32().ShouldBe(1);
        result.GetProperty("Success").GetBoolean().ShouldBeFalse();
        Files(result, "FilesPlanned")[0].GetProperty("Error").GetString().ShouldContain("Refusing to write");
    }

    [Fact]
    public void DryRun_PredictionMatchesWhatTheRealRunThenDoes()
    {
        // The two paths share FileWriter.Decide; this pins that they cannot drift apart.
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        var changed = Configure("sealed class {{ item.Name }} {}");
        var predicted = Json(GenerateTool.Execute(changed, dryRun: true));
        var actual = Json(GenerateTool.Execute(changed));

        predicted.GetProperty("WouldOverwrite").GetInt32().ShouldBe(actual.GetProperty("Overwritten").GetInt32());
        predicted.GetProperty("WouldCreate").GetInt32().ShouldBe(actual.GetProperty("Created").GetInt32());
    }

    // --- check ---------------------------------------------------------------

    [Fact]
    public void Check_AfterAGenerate_IsUpToDate()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        var result = Json(CheckTool.Execute(ctx));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeTrue();
        result.GetProperty("Checked").GetInt32().ShouldBe(2);
        result.GetProperty("Stale").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void Check_NeverGenerated_ReportsEveryFileMissing()
    {
        var result = Json(CheckTool.Execute(Configure()));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeFalse();
        Files(result, "Stale").ShouldAllBe(f => f.GetProperty("Reason").GetString() == "Missing");
    }

    [Fact]
    public void Check_AfterAModelChange_ReportsTheStaleFile()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            {
              "Name": "Catalog",
              "Nodes": [
                { "Name": "Product",  "Kind": "Class" },
                { "Name": "Category", "Kind": "Class" },
                { "Name": "Supplier", "Kind": "Class" }
              ]
            }
            """);

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeFalse();
        var stale = Files(result, "Stale").ShouldHaveSingleItem();
        stale.GetProperty("RelativePath").GetString().ShouldBe("Supplier.cs");
        stale.GetProperty("Reason").GetString().ShouldBe("Missing");
    }

    [Fact]
    public void Check_AfterAHandEditToAGeneratedFile_ReportsItDiffering()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);
        File.WriteAllText(Path.Combine(_outputDir, "Product.cs"), "// someone edited this");

        var result = Json(CheckTool.Execute(ctx));

        var stale = Files(result, "Stale").ShouldHaveSingleItem();
        stale.GetProperty("RelativePath").GetString().ShouldBe("Product.cs");
        // The manifest knows what pondhawk wrote, so this is an edit rather than a stale render.
        stale.GetProperty("Reason").GetString().ShouldBe("EditedSinceGenerated");
        stale.GetProperty("TemplateKey").GetString().ShouldBe("entity");
    }

    [Fact]
    public void Check_DoesNotCallASkipExistingStubStale()
    {
        // The stub belongs to the developer the moment it exists. generate would not touch it,
        // so reporting it as drift would make check permanently red on every real project.
        var ctx = Configure(mode: "SkipExisting");
        GenerateTool.Execute(ctx);
        File.WriteAllText(Path.Combine(_outputDir, "Product.cs"), "// the developer's own code");

        var result = Json(CheckTool.Execute(ctx));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Check_WritesNothing()
    {
        var ctx = Configure();

        CheckTool.Execute(ctx);

        Directory.Exists(_outputDir).ShouldBeFalse();
    }

    [Fact]
    public void Check_ARenderFailure_IsNotReportedAsUpToDate()
    {
        File.WriteAllText(Path.Combine(_tempDir, "model.json"), """
            { "Name": "X", "Nodes": [ { "Name": "../escape", "Kind": "Class" } ] }
            """);

        var result = Json(CheckTool.Execute(Configure()));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeFalse();
        result.GetProperty("Failed").GetArrayLength().ShouldBe(1);
    }
}
