using Pondhawk.Generation.Rendering;
using Shouldly;

namespace Pondhawk.Generation.Tests.Rendering;

public class FileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public FileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_fw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void WriteFile_Always_CreatesNewFile()
    {
        var name = Path.Combine("test.cs");
        var path = Path.Combine(_tempDir, name);
        var result = FileWriter.WriteFile(_tempDir, name, "content", "Always");

        result.Action.ShouldBe("Created");
        File.Exists(path).ShouldBeTrue();
        File.ReadAllText(path).ShouldBe("content");
    }

    [Fact]
    public void WriteFile_Always_OverwritesExisting()
    {
        var name = Path.Combine("test.cs");
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "old");

        var result = FileWriter.WriteFile(_tempDir, name, "new", "Always");

        result.Action.ShouldBe("Overwritten");
        File.ReadAllText(path).ShouldBe("new");
    }

    [Fact]
    public void WriteFile_SkipExisting_SkipsExistingFile()
    {
        var name = Path.Combine("test.cs");
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "original");

        var result = FileWriter.WriteFile(_tempDir, name, "new", "SkipExisting");

        result.Action.ShouldBe("SkippedExisting");
        File.ReadAllText(path).ShouldBe("original");
    }

    [Fact]
    public void WriteFile_SkipExisting_CreatesNewFile()
    {
        var name = Path.Combine("new.cs");
        var path = Path.Combine(_tempDir, name);

        var result = FileWriter.WriteFile(_tempDir, name, "content", "SkipExisting");

        result.Action.ShouldBe("Created");
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void WriteFile_CreatesDirectories()
    {
        var name = Path.Combine("sub", "dir", "test.cs");
        var path = Path.Combine(_tempDir, name);

        FileWriter.WriteFile(_tempDir, name, "content", "Always");

        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void WriteFile_Utf8NoBom()
    {
        var name = Path.Combine("test.cs");
        var path = Path.Combine(_tempDir, name);
        FileWriter.WriteFile(_tempDir, name, "content", "Always");

        var bytes = File.ReadAllBytes(path);
        // UTF-8 BOM would be EF BB BF
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_EmptyContent_SkippedEmpty()
    {
        var name = Path.Combine("empty.cs");
        var path = Path.Combine(_tempDir, name);
        var result = FileWriter.WriteFile(_tempDir, name, "", "Always");

        result.Action.ShouldBe("SkippedEmpty");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_WhitespaceContent_SkippedEmpty()
    {
        var name = Path.Combine("ws.cs");
        var path = Path.Combine(_tempDir, name);
        var result = FileWriter.WriteFile(_tempDir, name, "   \n  \t  ", "Always");

        result.Action.ShouldBe("SkippedEmpty");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_ReturnsRelativePath()
    {
        var name = Path.Combine("sub", "result.cs");
        var path = Path.Combine(_tempDir, name);
        var result = FileWriter.WriteFile(_tempDir, name, "content", "Always");

        result.Path.ShouldBe(path);
    }

    [Fact]
    public void WriteFile_UnicodeContent_PreservedCorrectly()
    {
        var name = Path.Combine("unicode.cs");
        var path = Path.Combine(_tempDir, name);
        var content = "// Commentaire fran\u00e7ais \u2014 \u00e9l\u00e8ve";
        FileWriter.WriteFile(_tempDir, name, content, "Always");

        File.ReadAllText(path).ShouldBe(content);
    }

    // --- containment: output paths are rendered from node names, so they are only as
    // --- trustworthy as the input model.

    [Theory]
    [InlineData("../escaped.cs")]
    [InlineData("../../escaped.cs")]
    [InlineData("sub/../../escaped.cs")]
    public void WriteFile_RelativeEscape_Throws(string relativePath)
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => FileWriter.WriteFile(_tempDir, relativePath, "content", "Always"));

        ex.Message.ShouldContain("outside the output directory");
        Directory.GetParent(_tempDir)!.GetFiles("escaped.cs").ShouldBeEmpty();
    }

    [Fact]
    public void WriteFile_AbsolutePath_Throws()
    {
        // Path.Combine discards the root entirely when the second argument is rooted.
        var absolute = Path.Combine(Path.GetTempPath(), $"pondhawk_absolute_{Guid.NewGuid():N}.cs");

        Should.Throw<InvalidOperationException>(
            () => FileWriter.WriteFile(_tempDir, absolute, "content", "Always"));

        File.Exists(absolute).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_NestedRelativePathWithinRoot_IsAllowed()
    {
        var result = FileWriter.WriteFile(_tempDir, "a/b/../c/file.cs", "content", "Always");

        result.Action.ShouldBe("Created");
        File.Exists(Path.Combine(_tempDir, "a", "c", "file.cs")).ShouldBeTrue();
    }

    [Fact]
    public void ResolveContained_ReturnsFullPathForContainedInput()
    {
        FileWriter.ResolveContained(_tempDir, "sub/file.cs")
            .ShouldBe(Path.GetFullPath(Path.Combine(_tempDir, "sub", "file.cs")));
    }

    [Fact]
    public void WriteFile_LeavesNoScratchFilesBehind()
    {
        // The temporary has to share the destination's directory for the rename to be atomic,
        // which puts it inside the output tree — so it must never survive the write.
        FileWriter.WriteFile(_tempDir, "Sub/Product.cs", "content", "Always");

        Directory.EnumerateFiles(_tempDir, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        File.ReadAllText(Path.Combine(_tempDir, "Sub", "Product.cs")).ShouldBe("content");
    }

    [Fact]
    public void WriteFile_ReplacingAFile_NeverLeavesAMixtureOfTheTwo()
    {
        const string name = "Product.cs";
        var path = Path.Combine(_tempDir, name);
        FileWriter.WriteFile(_tempDir, name, new string('a', 40_000), "Always");

        FileWriter.WriteFile(_tempDir, name, "short", "Always");

        File.ReadAllText(path).ShouldBe("short");
        Directory.EnumerateFiles(_tempDir, "*.tmp").ShouldBeEmpty();
    }
}
