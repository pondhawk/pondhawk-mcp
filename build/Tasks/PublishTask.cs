using Cake.Common.Diagnostics;
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
            context.DotNetPublish("src/Pondhawk.Generation.Mcp", new DotNetPublishSettings
            {
                Configuration = context.Configuration,
                Runtime = rid,
                SelfContained = true,
                OutputDirectory = $"publish/{rid}",
                ArgumentCustomization = args =>
                {
                    args = args
                        .Append("-p:PublishSingleFile=true")
                        .Append("-p:IncludeNativeLibrariesForSelfExtract=true")
                        .Append("-p:EnableCompressionInSingleFile=true")
                        .Append("-p:DebugType=embedded");

                    // The binary reports this to every client in the MCP handshake, so a
                    // release must be stamped with the tag it was cut from.
                    if (context.Version is { } version)
                        args = args.Append($"-p:Version={version}");

                    return args;
                }
            });
        }

        foreach (var rid in Rids)
        {
            context.CopyFile("docs/guide.html", $"publish/{rid}/guide.html");
        }

        context.Information(context.Version is { } stamped
            ? $"Published version {stamped}."
            : "Published a local build — no --release-version given, so the binaries report 0.0.0-dev.");
    }
}
