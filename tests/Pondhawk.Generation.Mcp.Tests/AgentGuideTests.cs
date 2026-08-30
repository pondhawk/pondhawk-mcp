using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Pondhawk.Generation.Mcp;
using Pondhawk.Generation.Mcp.Tools;
using Shouldly;

namespace Pondhawk.Generation.Mcp.Tests;

public class AgentGuideTests
{
    private static readonly Type[] ToolTypes =
    [
        typeof(InitTool),
        typeof(GenerateTool),
        typeof(CheckTool),
        typeof(ListTemplatesTool),
        typeof(ValidateConfigTool),
        typeof(UpdateTool)
    ];

    // --- ServerInstructions --------------------------------------------------

    [Fact]
    public void ServerInstructions_NamesTheThreeFilesAProjectIsMadeOf()
    {
        var instructions = AgentGuide.ServerInstructions;

        instructions.ShouldContain("model.json");
        instructions.ShouldContain("templates");
        instructions.ShouldContain("pondhawk.project.json");
    }

    [Fact]
    public void ServerInstructions_DescribeTheValidateThenGenerateLoop()
    {
        var instructions = AgentGuide.ServerInstructions;

        instructions.ShouldContain("validate_config");
        instructions.ShouldContain("generate");
    }

    [Fact]
    public void ServerInstructions_WarnAboutPartialGenerationFailure()
    {
        // The failure mode this guards is a caller treating any returned result as success.
        var instructions = AgentGuide.ServerInstructions;

        instructions.ShouldContain("Failed");
        instructions.ShouldContain("Success");
    }

    [Fact]
    public void ServerInstructions_PointAtTheResourceForTheFullGuide()
    {
        AgentGuide.ServerInstructions.ShouldContain(AgentGuide.ResourceUri);
    }

    [Fact]
    public void ServerInstructions_RenderLiquidTagsLiterally()
    {
        // ServerInstructions is a $$-interpolated raw string so that a lone brace is literal.
        // Get that wrong and the Liquid example silently becomes an interpolation hole.
        var instructions = AgentGuide.ServerInstructions;

        instructions.ShouldContain("{% dispatch node %}");
        instructions.ShouldNotContain("{{ResourceUri}}");
    }

    [Fact]
    public void ServerInstructions_StayShortEnoughToCarryInEveryClientContext()
    {
        // Sent on every connection and held for the whole session. The full guide is the
        // resource; this is the orientation. Kept well under the guide's own length.
        AgentGuide.ServerInstructions.Length.ShouldBeLessThan(AgentGuide.Markdown.Length);
        AgentGuide.ServerInstructions.Length.ShouldBeLessThan(4000);
    }

    [Fact]
    public void ServerInstructions_StandAloneWithoutTheFullGuide()
    {
        // A client that reads no resource and opens no file still has to be able to work.
        var instructions = AgentGuide.ServerInstructions;

        instructions.ShouldContain("Kind");
        instructions.ShouldContain("Liquid");
    }

    // --- The resource --------------------------------------------------------

    [Fact]
    public void Resource_ServesTheSameTextInitWritesToDisk()
    {
        AgentGuideResource.AgentsMarkdown().ShouldBe(AgentGuide.Markdown);
    }

    [Fact]
    public void Resource_IsDeclaredWithTheAdvertisedUriAndMarkdownMimeType()
    {
        var attribute = typeof(AgentGuideResource)
            .GetMethod(nameof(AgentGuideResource.AgentsMarkdown))!
            .GetCustomAttribute<McpServerResourceAttribute>()
            .ShouldNotBeNull();

        attribute.UriTemplate.ShouldBe(AgentGuide.ResourceUri);
        attribute.MimeType.ShouldBe("text/markdown");
    }

    [Fact]
    public void Resource_TypeIsDiscoverableAsAResourceType()
    {
        typeof(AgentGuideResource)
            .GetCustomAttribute<McpServerResourceTypeAttribute>()
            .ShouldNotBeNull();
    }

    [Fact]
    public void Guide_DescribesDispatchAndOverrides()
    {
        var guide = AgentGuide.Markdown;

        guide.ShouldContain("dispatch");
        guide.ShouldContain("Overrides");
        guide.ShouldContain("Variant");
    }

    // --- Tool descriptions ---------------------------------------------------

    [Theory]
    [MemberData(nameof(ToolMethods))]
    public void EveryTool_HasADescription(string toolName, MethodInfo method)
    {
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;

        description.ShouldNotBeNullOrWhiteSpace($"Tool '{toolName}' has no description.");
    }

    [Theory]
    [MemberData(nameof(ToolMethods))]
    public void NoTool_DefersToAFileTheClientCannotReach(string toolName, MethodInfo method)
    {
        // Descriptions used to end with "See AGENTS.md for detailed usage instructions."
        // The client is never told the project directory, so that pointer went nowhere.
        // The guide is served as a resource now; descriptions say what the tool returns.
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.ShouldNotContain("See AGENTS.md", Case.Insensitive,
            $"Tool '{toolName}' points at a file the client cannot open.");
    }

    [Theory]
    [MemberData(nameof(ToolMethods))]
    public void EveryTool_SaysWhatItReturns(string toolName, MethodInfo method)
    {
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.ShouldContain("Returns", Case.Insensitive,
            $"Tool '{toolName}' does not describe its result.");
    }

    public static TheoryData<string, MethodInfo> ToolMethods()
    {
        var data = new TheoryData<string, MethodInfo>();
        foreach (var type in ToolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var tool = method.GetCustomAttribute<McpServerToolAttribute>();
                if (tool is not null)
                {
                    data.Add(tool.Name ?? method.Name, method);
                }
            }
        }

        return data;
    }
}
