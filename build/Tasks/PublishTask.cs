using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

[TaskName("Publish")]
[IsDependentOn(typeof(TestTask))]
public sealed class PublishTask : FrostingTask<BuildContext>
{
    private static readonly string[] Rids = ["win-x64", "osx-arm64", "linux-x64", "linux-arm64"];

    public override void Run(BuildContext context)
    {
        foreach (var rid in Rids)
        {
            context.DotNetPublish("src/Pondhawk.Persistence.Mcp", new DotNetPublishSettings
            {
                Configuration = context.Configuration,
                Runtime = rid,
                SelfContained = true,
                OutputDirectory = $"publish/{rid}",
                ArgumentCustomization = args => args
                    .Append("-p:PublishSingleFile=true")
                    .Append("-p:IncludeNativeLibrariesForSelfExtract=true")
                    .Append("-p:EnableCompressionInSingleFile=true")
                    .Append("-p:DebugType=embedded")
            });
        }

        foreach (var rid in Rids)
        {
            context.CopyFile("docs/guide.html", $"publish/{rid}/guide.html");
        }
    }
}
