using Cake.Core;
using Cake.Frosting;

return new CakeHost()
    .UseContext<BuildContext>()
    .Run(args);

public class BuildContext : FrostingContext
{
    public string SolutionPath { get; }
    public new string Configuration { get; }

    public BuildContext(ICakeContext context) : base(context)
    {
        SolutionPath = context.Arguments.GetArgument("solution") ?? "pondhawk-mcp.slnx";
        Configuration = context.Arguments.GetArgument("configuration") ?? "Release";
    }
}
