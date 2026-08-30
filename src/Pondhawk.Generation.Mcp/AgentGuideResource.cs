using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp;

/// <summary>
/// Serves the agent guide over MCP.
/// </summary>
/// <remarks>
/// Tool descriptions used to point at AGENTS.md on disk, which an agent has no way to reach:
/// the project directory arrives as a launch argument the client never sees, and before init
/// the file does not exist at all. Serving the embedded text instead makes the guide readable
/// from a bare protocol connection, and guarantees it describes the running binary rather than
/// whatever version init last wrote into the project.
/// </remarks>
[McpServerResourceType]
public sealed class AgentGuideResource
{
    [McpServerResource(
        UriTemplate = AgentGuide.ResourceUri,
        Name = "agents_md",
        Title = "pondhawk — Instructions for AI Agents",
        MimeType = "text/markdown")]
    [Description("The full pondhawk guide: the input model, template macros and dispatch, variants, configuration, overrides, and the validate/generate workflow.")]
    public static string AgentsMarkdown() => AgentGuide.Markdown;
}
