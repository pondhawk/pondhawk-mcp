using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

[TaskName("Coverage")]
[IsDependentOn(typeof(BuildTask))]
public sealed class CoverageTask : FrostingTask<BuildContext>
{
    // The xUnit v3 runner has no --coverage flag of its own, so the collector wraps the
    // test executable from the outside rather than running inside it.
    /// <summary>
    /// Assembly names -- not project names -- of the code the report covers.
    /// </summary>
    private static readonly string[] CoveredAssemblies =
    [
        "Pondhawk.Generation",
        "pondhawk-generation-mcp"
    ];

    private static readonly string[] TestProjects =
    [
        "tests/Pondhawk.Generation.Tests",
        "tests/Pondhawk.Generation.Mcp.Tests"
    ];

    public override void Run(BuildContext context)
    {
        context.CleanDirectory("coverage");
        context.EnsureDirectoryExists("coverage/raw");

        Exec(context, "tool restore");

        foreach (var project in TestProjects)
        {
            var name = new DirectoryPath(project).GetDirectoryName();
            Exec(context,
                $"dotnet-coverage collect --output-format cobertura --output coverage/raw/{name}.cobertura.xml " +
                $"-- dotnet run --project {project} --configuration {context.Configuration} --no-build");
        }

        // Merges the per-suite runs and keeps only the shipped assemblies. The MCP
        // server's assembly name is pondhawk-generation-mcp, not its project name --
        // spell it wrong and its coverage vanishes from the report without a warning.
        Exec(context,
            "reportgenerator " +
            "-reports:coverage/raw/*.cobertura.xml " +
            "-targetdir:coverage/report " +
            "-reporttypes:Html;TextSummary;Cobertura " +
            $"-assemblyfilters:{string.Join(';', CoveredAssemblies.Select(a => "+" + a))} " +
            "-filefilters:-**/obj/** " +
            "-classfilters:-System.*");

        VerifyAssembliesPresent(context);

        var summary = ReportSummary(context);
        context.Information(summary);
        context.Information("HTML report: coverage/report/index.html");

        EnforceThreshold(context, summary);
    }

    /// <summary>
    /// A filter that matches nothing produces a clean report of the wrong thing, so
    /// fail loudly rather than quietly reporting on a subset of the codebase.
    /// </summary>
    private static void VerifyAssembliesPresent(BuildContext context)
    {
        var summary = ReportSummary(context);
        var missing = CoveredAssemblies
            .Where(assembly => !summary.Contains(assembly, StringComparison.Ordinal))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new CakeException(
                $"No coverage was reported for: {string.Join(", ", missing)}. " +
                "The assembly filter in CoverageTask is likely out of date.");
        }
    }

    private static string ReportSummary(BuildContext context)
    {
        var path = context.File("coverage/report/Summary.txt").Path.FullPath;
        return context.FileExists(path)
            ? System.IO.File.ReadAllText(path)
            : throw new CakeException("Coverage ran but produced no summary at coverage/report/Summary.txt.");
    }

    private static void EnforceThreshold(BuildContext context, string summary)
    {
        if (context.CoverageThreshold is not { } threshold)
        {
            return;
        }

        var match = Regex.Match(summary, @"Line coverage:\s*(\d+(?:[.,]\d+)?)%");
        if (!match.Success)
        {
            throw new CakeException("Could not read line coverage from the report summary.");
        }

        var actual = double.Parse(
            match.Groups[1].Value.Replace(',', '.'),
            CultureInfo.InvariantCulture);

        if (actual < threshold)
        {
            throw new CakeException(
                $"Line coverage {actual:0.0}% is below the required {threshold:0.0}%.");
        }

        context.Information($"Line coverage {actual:0.0}% meets the required {threshold:0.0}%.");
    }

    private static void Exec(BuildContext context, string arguments)
    {
        var exitCode = context.StartProcess("dotnet", new ProcessSettings
        {
            Arguments = arguments
        });

        if (exitCode != 0)
        {
            throw new CakeException($"'dotnet {arguments}' failed with exit code {exitCode}.");
        }
    }
}
