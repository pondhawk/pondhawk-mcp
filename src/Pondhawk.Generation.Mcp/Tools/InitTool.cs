using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Pondhawk.Generation.Configuration;
using Pondhawk.Generation.Manifest;
using Pondhawk.Generation.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Generation.Mcp.Tools;

[McpServerToolType]
public sealed class InitTool
{
    [McpServerTool(Name = "init"), Description("Scaffolds a new pondhawk project: pondhawk.project.json, a starter model.json, two example Liquid templates, JSON schemas, AGENTS.md, and .env. The examples generate Markdown and exist to demonstrate the mechanics — dispatch, macros, the two-file pattern — not to be a starting point for your real templates. Write those for your own target. Returns the list of files created. Fails if the project is already initialized.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Project name, recorded in the config and available as {{ model.Name }}. Default: MyProject")]
        string projectName = "MyProject",
        [Description("Root directory for generated files, relative to the project. Default: generated")]
        string outputDir = "generated")
    {
        var (logger, sw) = ctx.StartToolCall("init", $"projectName={projectName}");
        var configPath = ctx.ConfigPath;

        if (File.Exists(configPath))
        {
            logger.LogError("Tool init failed — pondhawk.project.json already exists");
            throw new InvalidOperationException("pondhawk.project.json already exists. Use validate_config to check the existing configuration.");
        }

        var config = new ProjectConfiguration
        {
            Schema_ = "./pondhawk.project.schema.json",
            ProjectName = projectName,
            OutputDir = outputDir,
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["reference"] = new()
                {
                    Path = "templates/reference.liquid",
                    OutputPattern = "{{ item.Name | pascal_case }}.generated.md",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = "Section"
                },
                ["notes"] = new()
                {
                    Path = "templates/notes.liquid",
                    OutputPattern = "{{ item.Name | pascal_case }}.notes.md",
                    Scope = "PerItem",
                    Mode = "SkipExisting",
                    AppliesTo = "Section"
                }
            },
            Values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Owner"] = "MyTeam"
            },
            Logging = new LoggingConfig { Enabled = false }
        };

        ProjectConfigurationLoader.Save(configPath, config);

        var utf8NoBom = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "pondhawk.project.schema.json"), ProjectConfigurationSchema.SchemaJson, utf8NoBom);
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "model.schema.json"), ModelFileSchema.SchemaJson, utf8NoBom);

        if (!File.Exists(ctx.ModelPath))
            File.WriteAllText(ctx.ModelPath, GetStarterModel(projectName), utf8NoBom);

        var templatesDir = Path.Combine(ctx.ProjectDir, "templates");
        Directory.CreateDirectory(templatesDir);
        File.WriteAllText(Path.Combine(templatesDir, "reference.liquid"), GetGeneratedTemplate(), utf8NoBom);
        File.WriteAllText(Path.Combine(templatesDir, "notes.liquid"), GetStubTemplate(), utf8NoBom);

        File.WriteAllText(Path.Combine(ctx.ProjectDir, "AGENTS.md"), AgentGuide.ProjectRules(config) + AgentGuide.Markdown, utf8NoBom);

        // .pondhawk holds the manifest, which is committed, next to the logs, which are not.
        ManifestStore.EnsureLogsIgnored(ctx.ProjectDir);

        var envPath = Path.Combine(ctx.ProjectDir, ".env");
        if (!File.Exists(envPath))
            File.WriteAllText(envPath, GetEnvFile(), utf8NoBom);

        var filesCreated = new[]
        {
            "pondhawk.project.json",
            "pondhawk.project.schema.json",
            "model.json",
            "model.schema.json",
            "AGENTS.md",
            ".env",
            "templates/reference.liquid",
            "templates/notes.liquid",
            ".pondhawk/.gitignore"
        };

        sw.Stop();
        logger.LogInformation("Tool init completed in {Duration}ms — {FileCount} files created", sw.ElapsedMilliseconds, filesCreated.Length);

        return JsonSerializer.Serialize(new
        {
            FilesCreated = filesCreated,
            NextSteps = "Read AGENTS.md, then run generate to see the example work end to end. The example templates render Markdown and are there to show the mechanics; replace them with templates for your own target rather than editing them into shape."
        });
    }

    /// <summary>
    /// A starter model deliberately not shaped like any particular target.
    /// </summary>
    /// <remarks>
    /// Section and Field carry no language baggage — they read equally as a form, a config
    /// schema, a struct or a document — which is the point. A scaffold that looked like C#
    /// entities would be a starter template pack by another name: it would freeze whatever was
    /// idiomatic when this binary was built, and invite editing into production rather than
    /// replacing.
    /// </remarks>
    private static string GetStarterModel(string projectName) => $$"""
        {
          "$schema": "./model.schema.json",
          "Name": "{{projectName}}",
          "Nodes": [
            {
              "Name": "Example",
              "Kind": "Section",
              "Description": "A placeholder node. Replace it, and its Kinds, with your own.",
              "Children": [
                { "Name": "Id",    "Kind": "Field", "Required": true,  "Description": "Unique identifier" },
                { "Name": "Title", "Kind": "Field", "Required": true,  "Description": "Display name" },
                { "Name": "Notes", "Kind": "Field", "Required": false, "Description": "Free text" }
              ]
            }
          ]
        }

        """;

    /// <summary>
    /// The Always half of the two-file pair. Demonstrates a macro per Kind, dispatch at both
    /// levels, a filter and a config value — the mechanics, in a format nobody ships.
    /// </summary>
    private static string GetGeneratedTemplate() => """
        {%- macro DefaultSection(s) -%}
        # {{ s.Name | pascal_case }}

        {{ s.Description }}
        {%- endmacro -%}
        {%- macro DefaultField(f) -%}
        | {{ f.Name }} | {% if f.Required %}yes{% else %}no{% endif %} | {{ f.Description }} |
        {%- endmacro -%}
        <!-- Generated by pondhawk. Rewritten on every run: edit the template, not this file. -->

        {% dispatch item %}

        | Field | Required | Description |
        | ----- | -------- | ----------- |
        {% for f in item.Children %}{% dispatch f %}
        {% endfor %}
        Maintained by {{ values.Owner }}.

        """;

    /// <summary>
    /// The SkipExisting half. Written once and then owned by whoever edits it — run generate
    /// twice and the pair reports Unchanged and SkippedExisting, which is the clearest way to
    /// see what Mode does.
    /// </summary>
    private static string GetStubTemplate() => """
        # {{ item.Name | pascal_case }} — notes

        Hand-written notes about {{ item.Name | pascal_case }}. Created once and never
        overwritten: this file is yours.

        """;

    private static string GetEnvFile() => """
        # Values referenced as ${VAR} from the Values section of pondhawk.project.json
        # are resolved from this file. Keep it out of version control.

        """;
}
