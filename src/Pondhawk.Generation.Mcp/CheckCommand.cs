using System.Text.Json;
using Pondhawk.Generation.Mcp.Tools;

namespace Pondhawk.Generation.Mcp;

/// <summary>
/// Runs `check` from a shell and reports through an exit code.
/// </summary>
/// <remarks>
/// The documentation tells projects to gate CI on the check being clean, which was advice a
/// build script could not take: the binary only spoke MCP over stdio, so running the check
/// meant writing an MCP client. Persuading agents not to hand-write generated files is the
/// weaker half of that story; a failing build is the half that holds, and it needs a command.
/// </remarks>
public static class CheckCommand
{
    public const int Clean = 0;
    public const int NotClean = 1;
    public const int CouldNotRun = 2;

    public static int Run(ServerContext ctx, TextWriter output, TextWriter error)
    {
        if (!File.Exists(ctx.ConfigPath))
        {
            error.WriteLine(
                $"pondhawk check could not run: no pondhawk.project.json in {ctx.ProjectDir}. "
                + "This is not a pondhawk project, or --project points at the wrong directory.");
            return CouldNotRun;
        }

        string json;
        try
        {
            json = CheckTool.Execute(ctx);
        }
        catch (Exception ex)
        {
            // A project that cannot be checked is a different answer from one that is dirty,
            // and a build script should be able to tell them apart.
            error.WriteLine($"pondhawk check could not run: {ex.Message}");
            return CouldNotRun;
        }

        var result = JsonDocument.Parse(json).RootElement;
        var clean = result.GetProperty("Clean").GetBoolean();

        output.WriteLine($"pondhawk check: {result.GetProperty("Summary").GetString()}");

        Report(output, result, "Stale", "stale", e =>
            $"{e.GetProperty("RelativePath").GetString()} — {e.GetProperty("Reason").GetString()}: {e.GetProperty("Detail").GetString()}");

        Report(output, result, "Orphans", "no longer produced", e =>
            $"{e.GetProperty("RelativePath").GetString()} (was {e.GetProperty("Template").GetString()}/{e.GetProperty("Node").GetString()})");

        Report(output, result, "Untracked", "not written by pondhawk", e =>
            $"{e.GetProperty("RelativePath").GetString()} — {e.GetProperty("Detail").GetString()}");

        Report(output, result, "Failed", "failed to render", e =>
            $"{e.GetProperty("Reference").GetString()} — {e.GetProperty("Error").GetString()}");

        return clean ? Clean : NotClean;
    }

    private static void Report(
        TextWriter output, JsonElement result, string property, string heading, Func<JsonElement, string> describe)
    {
        var entries = result.GetProperty(property).EnumerateArray().ToList();
        if (entries.Count == 0)
            return;

        output.WriteLine();
        output.WriteLine($"{entries.Count} {heading}:");
        foreach (var entry in entries)
            output.WriteLine($"  {describe(entry)}");
    }
}
