using System.Diagnostics;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests;

/// <summary>
/// The shell entry point CI gates on, exercised through the real binary.
/// </summary>
/// <remarks>
/// Documentation telling projects to fail the build when generated files drift is only worth
/// anything if a build script can actually run the check. Testing the exit code in process
/// would prove the logic and not the thing CI depends on, so this spawns the executable.
/// </remarks>
public class CheckCommandTests : IDisposable
{
    private readonly string _tempDir;

    public CheckCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pondhawk_cli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static string Executable => Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "pondhawk-generation-mcp.exe" : "pondhawk-generation-mcp");

    private (int ExitCode, string Output, string Error) RunCheck()
    {
        using var process = Process.Start(new ProcessStartInfo(Executable)
        {
            ArgumentList = { "--project", _tempDir, "--check" },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 30_000).ShouldBeTrue("the check must terminate, not wait for a protocol client");

        return (process.ExitCode, output, error);
    }

    private void Scaffold()
    {
        InitTool.Execute(new ServerContext(_tempDir), outputDir: "generated");
        GenerateTool.Execute(new ServerContext(_tempDir));
    }

    [Fact]
    public void ACleanProjectExitsZero()
    {
        Scaffold();

        var (exitCode, output, _) = RunCheck();

        exitCode.ShouldBe(CheckCommand.Clean);
        output.ShouldContain("Clean");
    }

    [Fact]
    public void AnEditedGeneratedFileFailsTheBuild()
    {
        Scaffold();
        File.WriteAllText(Path.Combine(_tempDir, "generated", "Example.generated.md"), "# hand edited");

        var (exitCode, output, _) = RunCheck();

        exitCode.ShouldBe(CheckCommand.NotClean);
        output.ShouldContain("Example.generated.md");
        output.ShouldContain("EditedSinceGenerated");
    }

    [Fact]
    public void AHandWrittenFileInTheOutputDirectoryFailsTheBuild()
    {
        // The bypass the whole enforcement story is about.
        Scaffold();
        File.WriteAllText(Path.Combine(_tempDir, "generated", "Sneaked.md"), "# written by hand");

        var (exitCode, output, _) = RunCheck();

        exitCode.ShouldBe(CheckCommand.NotClean);
        output.ShouldContain("not written by pondhawk");
        output.ShouldContain("Sneaked.md");
    }

    [Fact]
    public void AStaleTreeFailsTheBuild()
    {
        Scaffold();
        File.Delete(Path.Combine(_tempDir, "generated", "Example.generated.md"));

        var (exitCode, output, _) = RunCheck();

        exitCode.ShouldBe(CheckCommand.NotClean);
        output.ShouldContain("Missing");
    }

    [Fact]
    public void ANonProjectIsDistinguishedFromADirtyOne()
    {
        // A build script should be able to tell "this is broken" from "this needs regenerating".
        var (exitCode, _, error) = RunCheck();

        exitCode.ShouldBe(CheckCommand.CouldNotRun);
        error.ShouldContain("no pondhawk.project.json");
    }

    [Fact]
    public void TheServerStillStartsWithoutTheFlag()
    {
        // --check returns before the host starts; without it the binary must still be a server.
        Scaffold();

        using var process = Process.Start(new ProcessStartInfo(Executable)
        {
            ArgumentList = { "--project", _tempDir },
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        })!;

        try
        {
            process.WaitForExit(milliseconds: 1500).ShouldBeFalse("without --check it should wait for a client");
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
