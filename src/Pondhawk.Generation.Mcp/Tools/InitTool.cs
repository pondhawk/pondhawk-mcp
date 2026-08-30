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
    [McpServerTool(Name = "init"), Description("Scaffolds a new pondhawk project: pondhawk.project.json, a starter model.json, example Liquid templates, JSON schemas, AGENTS.md, and .env. Returns the list of files created. Fails if the project is already initialized.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Project name, recorded in the config and available as {{ model.Name }}. Default: MyProject")]
        string projectName = "MyProject",
        [Description("Root directory for generated files, relative to the project. Default: src/Generated")]
        string outputDir = "src/Generated",
        [Description("Value bound to {{ values.Namespace }} in the example templates. Default: MyProject.Generated")]
        string @namespace = "MyProject.Generated")
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
                ["entity"] = new()
                {
                    Path = "templates/entity.generated.liquid",
                    OutputPattern = "{{ item.Name | pascal_case }}.generated.cs",
                    Scope = "PerItem",
                    Mode = "Always",
                    AppliesTo = "Class"
                },
                ["entity-stub"] = new()
                {
                    Path = "templates/entity.stub.liquid",
                    OutputPattern = "{{ item.Name | pascal_case }}.cs",
                    Scope = "PerItem",
                    Mode = "SkipExisting",
                    AppliesTo = "Class"
                }
            },
            Values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Namespace"] = @namespace
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
        File.WriteAllText(Path.Combine(templatesDir, "entity.generated.liquid"), GetGeneratedTemplate(), utf8NoBom);
        File.WriteAllText(Path.Combine(templatesDir, "entity.stub.liquid"), GetStubTemplate(), utf8NoBom);

        File.WriteAllText(Path.Combine(ctx.ProjectDir, "AGENTS.md"), AgentGuide.Markdown, utf8NoBom);

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
            "templates/entity.generated.liquid",
            "templates/entity.stub.liquid",
            ".pondhawk/.gitignore"
        };

        sw.Stop();
        logger.LogInformation("Tool init completed in {Duration}ms — {FileCount} files created", sw.ElapsedMilliseconds, filesCreated.Length);

        return JsonSerializer.Serialize(new
        {
            FilesCreated = filesCreated,
            NextSteps = "Read AGENTS.md. Edit model.json to describe what to generate, adjust the templates, then run generate."
        });
    }

    private static string GetStarterModel(string projectName) => $$"""
        {
          "$schema": "./model.schema.json",
          "Name": "{{projectName}}",
          "Nodes": [
            {
              "Name": "Product",
              "Kind": "Class",
              "Note": "Replace this with your own nodes.",
              "Children": [
                { "Name": "Id",    "Kind": "Property", "Type": "int",     "IsNullable": false },
                { "Name": "Name",  "Kind": "Property", "Type": "string",  "IsNullable": false },
                { "Name": "Price", "Kind": "Property", "Type": "decimal", "IsNullable": false }
              ]
            }
          ]
        }

        """;

    private static string GetGeneratedTemplate() => """
        // <auto-generated>
        // This file was generated by pondhawk. Do not edit manually.
        // Any changes will be overwritten on next generation.
        // </auto-generated>

        namespace {{ values.Namespace }};
        {%- macro DefaultClass(c) %}
        public partial class {{ c.Name | pascal_case }}
        {%- endmacro %}
        {%- macro DefaultProperty(p) %}
            public {{ p.Type | type_nullable: p.IsNullable }} {{ p.Name | pascal_case }} { get; set; }
        {%- endmacro %}
        {% dispatch item %}
        {
        {%- for p in item.Children %}
        {%- dispatch p %}
        {%- endfor %}
        }

        """;

    private static string GetStubTemplate() => """
        namespace {{ values.Namespace }};

        // Hand-written half of {{ item.Name | pascal_case }}. Created once and never overwritten —
        // put custom logic, computed members and validation here.
        public partial class {{ item.Name | pascal_case }}
        {
        }

        """;

    private static string GetEnvFile() => """
        # Values referenced as ${VAR} from the Values section of pondhawk.project.json
        # are resolved from this file. Keep it out of version control.

        """;
}
