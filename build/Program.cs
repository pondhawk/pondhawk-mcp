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
    /// Minimum line coverage the Coverage target requires, from --threshold.
    /// Null means report the number without gating on it.
    /// </summary>
    public double? CoverageThreshold { get; }

    public BuildContext(ICakeContext context) : base(context)
    {
        SolutionPath = context.Arguments.GetArgument("solution") ?? "pondhawk-generation.slnx";
        Configuration = context.Arguments.GetArgument("configuration") ?? "Release";

        var threshold = context.Arguments.GetArgument("threshold");
        CoverageThreshold = threshold is null
            ? null
            : double.Parse(threshold, CultureInfo.InvariantCulture);
    }
}
