using Pondhawk.Persistence.Core.Rendering;
using Shouldly;

namespace Pondhawk.Persistence.Core.Tests.Rendering;

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
        var path = Path.Combine(_tempDir, "test.cs");
        var result = FileWriter.WriteFile(path, "content", "Always");

        result.Action.ShouldBe("Created");
        File.Exists(path).ShouldBeTrue();
        File.ReadAllText(path).ShouldBe("content");
    }

    [Fact]
    public void WriteFile_Always_OverwritesExisting()
    {
        var path = Path.Combine(_tempDir, "test.cs");
        File.WriteAllText(path, "old");

        var result = FileWriter.WriteFile(path, "new", "Always");

        result.Action.ShouldBe("Overwritten");
        File.ReadAllText(path).ShouldBe("new");
    }

    [Fact]
    public void WriteFile_SkipExisting_SkipsExistingFile()
    {
        var path = Path.Combine(_tempDir, "test.cs");
        File.WriteAllText(path, "original");

        var result = FileWriter.WriteFile(path, "new", "SkipExisting");

        result.Action.ShouldBe("SkippedExisting");
        File.ReadAllText(path).ShouldBe("original");
    }

    [Fact]
    public void WriteFile_SkipExisting_CreatesNewFile()
    {
        var path = Path.Combine(_tempDir, "new.cs");

        var result = FileWriter.WriteFile(path, "content", "SkipExisting");

        result.Action.ShouldBe("Created");
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void WriteFile_CreatesDirectories()
    {
        var path = Path.Combine(_tempDir, "sub", "dir", "test.cs");

        FileWriter.WriteFile(path, "content", "Always");

        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void WriteFile_Utf8NoBom()
    {
        var path = Path.Combine(_tempDir, "test.cs");
        FileWriter.WriteFile(path, "content", "Always");

        var bytes = File.ReadAllBytes(path);
        // UTF-8 BOM would be EF BB BF
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_EmptyContent_SkippedEmpty()
    {
        var path = Path.Combine(_tempDir, "empty.cs");
        var result = FileWriter.WriteFile(path, "", "Always");

        result.Action.ShouldBe("SkippedEmpty");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_WhitespaceContent_SkippedEmpty()
    {
        var path = Path.Combine(_tempDir, "ws.cs");
        var result = FileWriter.WriteFile(path, "   \n  \t  ", "Always");

        result.Action.ShouldBe("SkippedEmpty");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void WriteFile_ReturnsRelativePath()
    {
        var path = Path.Combine(_tempDir, "sub", "result.cs");
        var result = FileWriter.WriteFile(path, "content", "Always");

        result.Path.ShouldBe(path);
    }

    [Fact]
    public void WriteFile_UnicodeContent_PreservedCorrectly()
    {
        var path = Path.Combine(_tempDir, "unicode.cs");
        var content = "// Commentaire fran\u00e7ais \u2014 \u00e9l\u00e8ve";
        FileWriter.WriteFile(path, content, "Always");

        File.ReadAllText(path).ShouldBe(content);
    }
}
