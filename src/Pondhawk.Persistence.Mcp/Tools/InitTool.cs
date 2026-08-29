using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Pondhawk.Persistence.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class InitTool
{
    [McpServerTool(Name = "init"), Description("Scaffolds a new pondhawk project: pondhawk.project.json, a starter model.json, example Liquid templates, JSON schemas, AGENTS.md, and .env. See AGENTS.md for detailed usage instructions.")]
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

        File.WriteAllText(Path.Combine(ctx.ProjectDir, "AGENTS.md"), GetAgentsMarkdown(), utf8NoBom);

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
            "templates/entity.stub.liquid"
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

        {% dispatch item %}
        {

        {%- macro DefaultProperty(p) %}
            public {{ p.Type | type_nullable: p.IsNullable }} {{ p.Name | pascal_case }} { get; set; }
        {%- endmacro %}

        {%- for p in item.Children %}
        {% dispatch p %}
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

    public static string GetAgentsMarkdown() => """
        # pondhawk — Instructions for AI Agents

        pondhawk renders Liquid templates against a structured input model and writes the result
        to disk. It exists for artifacts that must all follow the *same* pattern — a fleet of
        entities, DTOs, API clients, handlers, resources — where consistency across the set
        matters more than any individual file.

        It knows nothing about databases, or C#, or any particular target. What it generates is
        entirely determined by the model you write and the templates you author.

        ## Files

        | File | Purpose |
        |------|---------|
        | `pondhawk.project.json` | Configuration: templates, output directory, values, overrides |
        | `model.json` | The input model — what to generate |
        | `templates/*.liquid` | The templates — how to generate it |
        | `AGENTS.md` | This file |
        | `.env` | Values kept out of version control |

        ## The input model

        `model.json` holds a list of nodes. Every node has a `Name` and a `Kind`, and may have
        `Children`. Everything else you write on a node is metadata, and templates read it as an
        ordinary member — `"Type": "int"` is reached as `{{ p.Type }}`.

        ```json
        {
          "Name": "Catalog",
          "Nodes": [
            {
              "Name": "Product",
              "Kind": "Class",
              "Children": [
                { "Name": "Id",    "Kind": "Property", "Type": "int" },
                { "Name": "Price", "Kind": "Property", "Type": "decimal", "IsNullable": false }
              ]
            }
          ]
        }
        ```

        Nodes nest to any depth. Two levels suit a class with properties; three suit a resource
        with operations that have parameters.

        `Kind` is yours to choose. It names what the node is, and it selects the macro that
        renders it — see dispatch below.

        ## Templates

        Templates are [Liquid](https://shopify.github.io/liquid/), rendered by Fluid.

        Bound in every template:

        | Variable | Contents |
        |----------|----------|
        | `item` | The current node (PerItem scope only) |
        | `items` | All matching nodes (Single scope only) |
        | `model` | The model root — `{{ model.Name }}` plus any root metadata |
        | `values` | The `Values` section of the config |
        | `config` | The project configuration |
        | `parameters` | Key-values passed to the `generate` call |

        ### Macros and dispatch

        Write one macro per Kind, named `Default<Kind>`:

        ```liquid
        {%- macro DefaultProperty(p) %}
            public {{ p.Type }} {{ p.Name | pascal_case }} { get; set; }
        {%- endmacro %}

        {%- for p in item.Children %}
        {% dispatch p %}
        {%- endfor %}
        ```

        `{% dispatch p %}` looks at the node's Kind and calls the matching macro. A node of Kind
        `Property` renders through `DefaultProperty`; one of Kind `Operation` through
        `DefaultOperation`.

        This is what keeps a generated set consistent: every node of a Kind goes through one
        macro, so changing that macro changes every artifact at once.

        ### Variants

        When one node needs to render differently, define a variant macro — `<Variant><Kind>` —
        and point an override at the node:

        ```liquid
        {%- macro CurrencyProperty(p) %}
            [Column(TypeName = "decimal(18,2)")]
            public decimal {{ p.Name | pascal_case }} { get; set; }
        {%- endmacro %}
        ```

        ```json
        { "Path": "Product/Price", "Artifact": "entity", "Variant": "Currency" }
        ```

        Dispatch falls back to `Default<Kind>` when a variant macro is missing, so an override
        naming a macro you have not written yet degrades rather than breaking.

        ### Filters

        `pascal_case`, `camel_case`, `snake_case`, `pluralize`, `singularize`, plus
        `type_nullable` (appends `?` when a second argument is true), on top of Liquid's built-ins.

        ## Configuration

        ```json
        {
          "OutputDir": "src/Generated",
          "Templates": {
            "entity": {
              "Path": "templates/entity.generated.liquid",
              "OutputPattern": "{{ item.Name | pascal_case }}.generated.cs",
              "Scope": "PerItem",
              "Mode": "Always",
              "AppliesTo": "Class"
            }
          },
          "Values": { "Namespace": "MyProject.Generated" },
          "Overrides": []
        }
        ```

        - **Scope** — `PerItem` renders one file per matching node; `Single` renders one file for all.
        - **Mode** — `Always` overwrites every run; `SkipExisting` writes once and then leaves it alone.
        - **AppliesTo** — restricts a template to top-level nodes of one Kind. Omit for all.
        - **Values** — anything templates need. String values support `${VAR}` from `.env`.

        ### The two-file pattern

        Pair an `Always` template with a `SkipExisting` one to separate generated code from
        hand-written code. The generated file is overwritten freely; the stub is created once and
        is then the developer's. In C# these are `partial class` halves; other languages have
        their own equivalents.

        ## Overrides

        Overrides address nodes by path and change how they render for one artifact.

        ```json
        { "Path": "Product/Price", "Artifact": "entity", "Variant": "Currency" }
        { "Path": "*/CreatedAt",   "Artifact": "entity", "Ignore": true }
        { "Path": "Order/**",      "Artifact": "dto",    "Metadata": { "Access": "internal" } }
        ```

        - `Path` — slash-delimited. `*` matches one node, `**` matches any depth.
        - `Artifact` — the template key. Required when `Variant` is set; omit to apply everywhere.
        - `Variant` — the macro variant to render with.
        - `Ignore` — drops matched nodes from that artifact.
        - `Metadata` — merged onto matched nodes, overwriting what the model declared.

        When several overrides match one node, the one with the most literal path segments wins.
        Ties go to whichever is listed later, so state the broad rule first and narrow afterwards.

        ## Tools

        | Tool | Purpose |
        |------|---------|
        | `init` | Scaffolds a new project |
        | `generate` | Renders templates and writes files |
        | `list_templates` | Lists configured templates |
        | `validate_config` | Checks config, templates and model without generating |
        | `update` | Refreshes AGENTS.md and JSON schemas after upgrading pondhawk |

        ## Working on a pondhawk project

        1. Edit `model.json` to describe what to generate.
        2. Author templates, one `Default<Kind>` macro per Kind.
        3. Run `validate_config` — it reports unparseable templates, unknown filters, overrides
           matching no node, and templates whose `AppliesTo` matches no Kind in the model.
        4. Run `generate`.
        5. Read a generated file before declaring success. A template that renders empty is
           skipped silently, and an unknown metadata key renders as nothing rather than failing.
        """;
}
