using System.Globalization;
using Cake.Core;
using Cake.Frosting;

return new CakeHost()
    .UseContext<BuildContext>()
    .Run(args);

public class BuildContext : FrostingContext
{
    public string SolutionPath { get; }
    public new string Configuration { get; }

    /// <summary>
    /// Version stamped into the published binaries, from --release-version (Cake reserves
    /// --version for itself). Null leaves the default
    /// in Directory.Build.props, which marks the build as a local one rather than a release.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Minimum line coverage the Coverage target requires, from --threshold.
    /// Null means report the number without gating on it.
    /// </summary>
    public double? CoverageThreshold { get; }

    public BuildContext(ICakeContext context) : base(context)
    {
        SolutionPath = context.Arguments.GetArgument("solution") ?? "pondhawk-generation.slnx";
        Configuration = context.Arguments.GetArgument("configuration") ?? "Release";

        // Accepts either "2.0.0" or the tag it came from, "v2.0.0", so the release workflow can
        // pass ${{ github.ref_name }} through untouched.
        var version = context.Arguments.GetArgument("release-version");
        Version = string.IsNullOrWhiteSpace(version) ? null : version.TrimStart('v', 'V');

        var threshold = context.Arguments.GetArgument("threshold");
        CoverageThreshold = threshold is null
            ? null
            : double.Parse(threshold, CultureInfo.InvariantCulture);
    }
}
