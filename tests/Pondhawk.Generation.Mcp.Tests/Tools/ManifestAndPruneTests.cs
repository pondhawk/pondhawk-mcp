using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Manifest;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests.Tools;

public class ManifestAndPruneTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;

    public ManifestAndPruneTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_manifest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "templates"));
        _outputDir = Path.Combine(_tempDir, "output");
        WriteModel("Product", "Category");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void WriteModel(params string[] names)
    {
        var nodes = string.Join(",\n    ", names.Select(n => $$"""{ "Name": "{{n}}", "Kind": "Class" }"""));
        File.WriteAllText(Path.Combine(_tempDir, "model.json"), $$"""
            { "Name": "Catalog", "Nodes": [ {{nodes}} ] }
            """);
    }

    private ServerContext Configure(
        string template = "class {{ item.Name }} {}",
        string mode = "Always",
        string outputPattern = "{{ item.Name }}.cs",
        string? outputDir = null)
    {
        File.WriteAllText(Path.Combine(_tempDir, "templates", "entity.liquid"), template);

        ProjectConfigurationLoader.Save(Path.Combine(_tempDir, "pondhawk.project.json"), new ProjectConfiguration
        {
            OutputDir = outputDir ?? "output",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.liquid",
                    OutputPattern = outputPattern,
                    Scope = "PerItem",
                    Mode = mode
                }
            }
        });

        return new ServerContext(_tempDir);
    }

    private static JsonElement Json(string result) => JsonDocument.Parse(result).RootElement;
    private GenerationManifest Manifest() => ManifestStore.Load(_tempDir);
    private string ManifestText() => File.ReadAllText(ManifestStore.PathFor(_tempDir));

    // --- the manifest as a document ------------------------------------------

    [Fact]
    public void Generate_RecordsEveryFileItWrote()
    {
        GenerateTool.Execute(Configure());

        var manifest = Manifest();

        manifest.Files.Keys.ShouldBe(["Category.cs", "Product.cs"]);
        manifest.Files["Product.cs"].Template.ShouldBe("entity");
        manifest.Files["Product.cs"].Node.ShouldBe("Product");
        manifest.Files["Product.cs"].Model.ShouldBe("model.json");
        manifest.Files["Product.cs"].Mode.ShouldBe("Always");
        manifest.Files["Product.cs"].Hash.ShouldStartWith("sha256:");
    }

    [Fact]
    public void Manifest_RecordsTheConfiguredOutputDirNotAMachinePath()
    {
        // It is committed, so an absolute path would be one developer's machine and wrong on
        // every other clone.
        GenerateTool.Execute(Configure());

        Manifest().OutputDir.ShouldBe("output");
        ManifestText().ShouldNotContain(_tempDir);
    }

    [Fact]
    public void RegeneratingAnUnchangedProject_LeavesTheManifestByteIdentical()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);
        var first = ManifestText();

        GenerateTool.Execute(ctx);

        ManifestText().ShouldBe(first, "a committed manifest must stay quiet in git status");
    }

    [Fact]
    public void Manifest_IsOrderedByPath()
    {
        WriteModel("Zebra", "Apple", "Mango");
        GenerateTool.Execute(Configure());

        Manifest().Files.Keys.ShouldBe(["Apple.cs", "Mango.cs", "Zebra.cs"]);
    }

    [Fact]
    public void Manifest_SitsBesideAGitignoreThatExcludesTheLogsOnly()
    {
        GenerateTool.Execute(Configure());

        var rules = File.ReadAllLines(Path.Combine(_tempDir, ".pondhawk", ".gitignore"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        rules.ShouldContain("logs/");
        rules.ShouldNotContain(r => r.Contains("manifest.json"), "the manifest is meant to be committed");
    }

    [Fact]
    public void FilteredRun_MergesRatherThanReplacing()
    {
        // Generating one template must not orphan everything the others produced.
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        GenerateTool.Execute(ctx, items: ["Product"]);

        Manifest().Files.Keys.ShouldBe(["Category.cs", "Product.cs"]);
    }

    [Fact]
    public void AFileNoLongerProduced_KeepsItsEntry()
    {
        // The entry is the only evidence pondhawk wrote the file. Dropping it here would
        // destroy the proof that prune needs to delete it safely.
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        WriteModel("Product");
        GenerateTool.Execute(new ServerContext(_tempDir));

        Manifest().Files.Keys.ShouldContain("Category.cs");
    }

    [Fact]
    public void APreExistingSkipExistingFile_IsNotStampedWithOurHash()
    {
        // We did not write it, so recording a hash for it would erase the evidence that it
        // diverged from anything pondhawk produced.
        File.WriteAllText(Path.Combine(_tempDir, "pondhawk.project.json"), "{}");
        Directory.CreateDirectory(_outputDir);
        File.WriteAllText(Path.Combine(_outputDir, "Product.cs"), "// hand written, predates pondhawk");

        GenerateTool.Execute(Configure(mode: "SkipExisting"));

        Manifest().Files.ShouldNotContainKey("Product.cs");
        Manifest().Files.ShouldContainKey("Category.cs");
    }

    [Fact]
    public void CorruptManifest_DoesNotStopGeneration()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".pondhawk"));
        File.WriteAllText(ManifestStore.PathFor(_tempDir), "{ not json");

        var result = Json(GenerateTool.Execute(Configure()));

        result.GetProperty("Success").GetBoolean().ShouldBeTrue();
        Manifest().Files.Count.ShouldBe(2);
    }

    // --- write avoidance ------------------------------------------------------

    [Fact]
    public void Regenerating_DoesNotTouchAFileWhoseContentIsAlreadyCorrect()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        var path = Path.Combine(_outputDir, "Product.cs");
        var stamp = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(path, stamp);

        var result = Json(GenerateTool.Execute(ctx));

        result.GetProperty("Unchanged").GetInt32().ShouldBe(2);
        result.GetProperty("Overwritten").GetInt32().ShouldBe(0);
        File.GetLastWriteTimeUtc(path).ShouldBe(stamp, "rewriting identical content would wake every watcher downstream");
    }

    // --- check: orphans and diagnosis ----------------------------------------

    [Fact]
    public void Check_ReportsAFileTheModelNoLongerProduces()
    {
        GenerateTool.Execute(Configure());
        WriteModel("Product");

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeFalse();
        var orphan = result.GetProperty("Orphans").EnumerateArray().ShouldHaveSingleItem();
        orphan.GetProperty("RelativePath").GetString().ShouldBe("Category.cs");
        result.GetProperty("Summary").GetString()!.ShouldContain("prune");
    }

    [Fact]
    public void Check_DoesNotReportAnOrphanWhoseFileIsAlreadyGone()
    {
        GenerateTool.Execute(Configure());
        WriteModel("Product");
        File.Delete(Path.Combine(_outputDir, "Category.cs"));

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("Orphans").GetArrayLength().ShouldBe(0);
        result.GetProperty("UpToDate").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Check_SkipsOrphanDetectionWhenFiltered()
    {
        // A filtered check has no idea what the templates it did not run produce.
        GenerateTool.Execute(Configure());
        WriteModel("Product");

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir), templates: ["entity"]));

        result.GetProperty("Orphans").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void Check_DistinguishesAChangedModelFromAnEditedFile()
    {
        var ctx = Configure();
        GenerateTool.Execute(ctx);

        // The template moved on; the file is exactly as pondhawk left it.
        var changed = Configure("sealed class {{ item.Name }} {}");
        var inputsChanged = Json(CheckTool.Execute(changed));

        inputsChanged.GetProperty("Stale").EnumerateArray()
            .ShouldAllBe(f => f.GetProperty("Reason").GetString() == "InputsChanged");

        // Now somebody edits one of them.
        File.WriteAllText(Path.Combine(_outputDir, "Product.cs"), "// mine now");
        var edited = Json(CheckTool.Execute(changed));

        edited.GetProperty("Stale").EnumerateArray()
            .First(f => f.GetProperty("RelativePath").GetString() == "Product.cs")
            .GetProperty("Reason").GetString().ShouldBe("EditedSinceGenerated");
    }

    [Fact]
    public void Check_WithoutAManifest_SaysItCannotTellAnEditFromAStaleRender()
    {
        GenerateTool.Execute(Configure());
        File.Delete(ManifestStore.PathFor(_tempDir));

        var stale = Json(CheckTool.Execute(Configure("sealed class {{ item.Name }} {}")))
            .GetProperty("Stale").EnumerateArray().First();

        stale.GetProperty("Reason").GetString().ShouldBe("Differs");
        stale.GetProperty("Detail").GetString()!.ShouldContain("No manifest entry");
    }

    // --- prune ----------------------------------------------------------------

    [Fact]
    public void Prune_WithoutApply_ReportsAndDeletesNothing()
    {
        GenerateTool.Execute(Configure());
        WriteModel("Product");

        var result = Json(PruneTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("Pruned").GetInt32().ShouldBe(1);
        result.GetProperty("NothingWritten").GetBoolean().ShouldBeTrue();
        result.GetProperty("Summary").GetString()!.ShouldContain("would be deleted");
        File.Exists(Path.Combine(_outputDir, "Category.cs")).ShouldBeTrue();
    }

    [Fact]
    public void Prune_WithApply_DeletesTheFileAndItsEntry()
    {
        GenerateTool.Execute(Configure());
        WriteModel("Product");

        var result = Json(PruneTool.Execute(new ServerContext(_tempDir), apply: true));

        result.GetProperty("Pruned").GetInt32().ShouldBe(1);
        File.Exists(Path.Combine(_outputDir, "Category.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(_outputDir, "Product.cs")).ShouldBeTrue();
        Manifest().Files.Keys.ShouldBe(["Product.cs"]);
    }

    [Fact]
    public void Prune_NeverDeletesADeveloperOwnedFile()
    {
        GenerateTool.Execute(Configure(mode: "SkipExisting"));
        WriteModel("Product");

        var result = Json(PruneTool.Execute(new ServerContext(_tempDir), apply: true));

        result.GetProperty("Pruned").GetInt32().ShouldBe(0);
        File.Exists(Path.Combine(_outputDir, "Category.cs")).ShouldBeTrue();
        result.GetProperty("KeptFiles").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("Reason").GetString().ShouldBe("DeveloperFile");
    }

    [Fact]
    public void Prune_NeverDeletesAFileEditedSinceItWasGenerated()
    {
        GenerateTool.Execute(Configure());
        WriteModel("Product");
        File.WriteAllText(Path.Combine(_outputDir, "Category.cs"), "// someone put work in here");

        var result = Json(PruneTool.Execute(new ServerContext(_tempDir), apply: true));

        result.GetProperty("Pruned").GetInt32().ShouldBe(0);
        File.ReadAllText(Path.Combine(_outputDir, "Category.cs")).ShouldBe("// someone put work in here");
        result.GetProperty("KeptFiles").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("Reason").GetString().ShouldBe("EditedSinceGenerated");
    }

    [Fact]
    public void Prune_NeverDeletesAFileItDidNotRecord()
    {
        GenerateTool.Execute(Configure());
        File.WriteAllText(Path.Combine(_outputDir, "HandWritten.cs"), "// nothing to do with pondhawk");

        Json(PruneTool.Execute(new ServerContext(_tempDir), apply: true))
            .GetProperty("Pruned").GetInt32().ShouldBe(0);

        File.Exists(Path.Combine(_outputDir, "HandWritten.cs")).ShouldBeTrue();
    }

    [Fact]
    public void Prune_TakesTheFolderWhenItTakesTheLastFileInIt()
    {
        GenerateTool.Execute(Configure(outputPattern: "{{ item.Name }}/{{ item.Name }}.cs"));
        WriteModel("Product");

        PruneTool.Execute(new ServerContext(_tempDir), apply: true);

        Directory.Exists(Path.Combine(_outputDir, "Category")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_outputDir, "Product")).ShouldBeTrue();
    }

    [Fact]
    public void Prune_RefusesWhenOutputDirHasMoved()
    {
        GenerateTool.Execute(Configure());

        var result = Json(PruneTool.Execute(Configure(outputDir: "elsewhere"), apply: true));

        result.GetProperty("Refused").GetBoolean().ShouldBeTrue();
        result.GetProperty("Reason").GetString()!.ShouldContain("OutputDir has changed");
        File.Exists(Path.Combine(_outputDir, "Product.cs")).ShouldBeTrue();
    }

    [Fact]
    public void Prune_OnATreeThatIsCurrent_DoesNothing()
    {
        GenerateTool.Execute(Configure());

        var result = Json(PruneTool.Execute(new ServerContext(_tempDir), apply: true));

        result.GetProperty("Pruned").GetInt32().ShouldBe(0);
        result.GetProperty("Summary").GetString()!.ShouldContain("Nothing to prune");
        Directory.GetFiles(_outputDir).Length.ShouldBe(2);
    }

    [Fact]
    public void PruneThenCheck_LeavesTheProjectUpToDate()
    {
        GenerateTool.Execute(Configure());
        WriteModel("Product");
        PruneTool.Execute(new ServerContext(_tempDir), apply: true);

        Json(CheckTool.Execute(new ServerContext(_tempDir)))
            .GetProperty("UpToDate").GetBoolean().ShouldBeTrue();
    }

    // --- check: files pondhawk did not put there ------------------------------

    [Fact]
    public void Check_ReportsAHandWrittenFileInTheOutputDirectory()
    {
        // The bypass this catches: an agent decides it is simpler to write the file itself.
        // Every other check starts from the plan or the manifest, so such a file was invisible.
        GenerateTool.Execute(Configure());
        File.WriteAllText(Path.Combine(_outputDir, "Supplier.cs"), "// written by hand");

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        var untracked = result.GetProperty("Untracked").EnumerateArray().ShouldHaveSingleItem();
        untracked.GetProperty("RelativePath").GetString().ShouldBe("Supplier.cs");
        result.GetProperty("Summary").GetString()!.ShouldContain("untracked");
    }

    [Fact]
    public void Check_UntrackedFilesFailCleanButNotUpToDate()
    {
        // Staleness and trespass are different questions. Clean is the one to gate CI on.
        GenerateTool.Execute(Configure());
        File.WriteAllText(Path.Combine(_outputDir, "Supplier.cs"), "// written by hand");

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("UpToDate").GetBoolean().ShouldBeTrue();
        result.GetProperty("Clean").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Check_FindsAHandWrittenFileNestedInTheOutputTree()
    {
        GenerateTool.Execute(Configure());
        Directory.CreateDirectory(Path.Combine(_outputDir, "Sub"));
        File.WriteAllText(Path.Combine(_outputDir, "Sub", "Sneaky.cs"), "// hidden away");

        Json(CheckTool.Execute(new ServerContext(_tempDir)))
            .GetProperty("Untracked").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("RelativePath").GetString().ShouldBe(Path.Combine("Sub", "Sneaky.cs"));
    }

    [Fact]
    public void Check_DoesNotCallGeneratedOrOrphanedFilesUntracked()
    {
        // A file pondhawk wrote is accounted for either way — as current, or as an orphan.
        // Reporting it twice under different names would be noise.
        GenerateTool.Execute(Configure());
        WriteModel("Product");

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("Orphans").GetArrayLength().ShouldBe(1);
        result.GetProperty("Untracked").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void Check_ACleanTreeIsClean()
    {
        GenerateTool.Execute(Configure());

        var result = Json(CheckTool.Execute(new ServerContext(_tempDir)));

        result.GetProperty("Clean").GetBoolean().ShouldBeTrue();
        result.GetProperty("Untracked").GetArrayLength().ShouldBe(0);
        result.GetProperty("Summary").GetString()!.ShouldContain("Clean");
    }

    [Fact]
    public void Check_SkipsUntrackedDetectionWhenFiltered()
    {
        // A filtered check does not know what the other templates produce, so anything it has
        // not planned would look untracked.
        GenerateTool.Execute(Configure());
        File.WriteAllText(Path.Combine(_outputDir, "Supplier.cs"), "// written by hand");

        Json(CheckTool.Execute(new ServerContext(_tempDir), templates: ["entity"]))
            .GetProperty("Untracked").GetArrayLength().ShouldBe(0);
    }
}
