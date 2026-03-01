using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Run;
using Cake.Frosting;

[TaskName("Test")]
[IsDependentOn(typeof(BuildTask))]
public sealed class TestTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // xUnit v3 tests are standalone executables, run with dotnet run
        context.DotNetRun("tests/Pondhawk.Persistence.Core.Tests", settings: new DotNetRunSettings
        {
            Configuration = context.Configuration,
            NoBuild = true
        });

        context.DotNetRun("tests/Pondhawk.Persistence.Mcp.Tests", settings: new DotNetRunSettings
        {
            Configuration = context.Configuration,
            NoBuild = true
        });
    }
}
