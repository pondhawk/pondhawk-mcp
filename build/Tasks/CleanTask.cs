using Cake.Common.IO;
using Cake.Frosting;

[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // Exclude the build project's own directories (it's the running process)
        var binDirs = context.GetDirectories("**/bin") - context.GetDirectories("build/bin");
        var objDirs = context.GetDirectories("**/obj") - context.GetDirectories("build/obj");
        context.CleanDirectories(binDirs);
        context.CleanDirectories(objDirs);
        context.CleanDirectories("publish");
    }
}
