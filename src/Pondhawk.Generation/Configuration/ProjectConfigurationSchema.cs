using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Json.Schema;

namespace Pondhawk.Generation.Configuration;

public static class ProjectConfigurationSchema
{
    public const string SchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://pondhawk-mcp/pondhawk.project.schema.json",
          "title": "pondhawk Project Configuration",
          "description": "Configuration for template-driven artifact generation from model.json.",
          "type": "object",
          "properties": {
            "$schema": {
              "type": "string",
              "description": "Path to the JSON Schema file for IDE support."
            },
            "ProjectName": {
              "type": "string",
              "description": "Project name, for display purposes."
            },
            "Description": {
              "type": "string",
              "description": "Free-text description of what this project generates."
            },
            "OutputDir": {
              "type": "string",
              "description": "Root directory for generated files, relative to the project directory."
            },
            "Templates": {
              "type": "object",
              "description": "Templates to render, keyed by artifact name. The key is the artifact name that overrides target.",
              "additionalProperties": { "$ref": "#/$defs/TemplateConfig" },
              "minProperties": 1
            },
            "Values": {
              "type": "object",
              "description": "Project-wide values available to every template as {{ values.X }}. String values support ${VAR} substitution from .env."
            },
            "Partials": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Liquid files whose macros every template shares, applied in order ahead of each template. A template declaring the same macro shadows the shared one."
            },
            "Overrides": {
              "type": "array",
              "description": "Rules that change how matched nodes render for a given artifact.",
              "items": { "$ref": "#/$defs/OverrideConfig" }
            },
            "Logging": { "$ref": "#/$defs/LoggingConfig" }
          },
          "required": ["OutputDir", "Templates"],
          "additionalProperties": false,
          "$defs": {
            "TemplateConfig": {
              "type": "object",
              "properties": {
                "Path": {
                  "type": "string",
                  "description": "Path to the Liquid template, relative to the project directory."
                },
                "OutputPattern": {
                  "type": "string",
                  "description": "Liquid expression producing the output path, relative to OutputDir. PerItem templates can use {{ item.Name }}."
                },
                "Scope": {
                  "type": "string",
                  "enum": ["PerItem", "Single"],
                  "description": "PerItem renders once per matching node; Single renders one file for all of them."
                },
                "Mode": {
                  "type": "string",
                  "enum": ["Always", "SkipExisting"],
                  "description": "Always overwrites on every run; SkipExisting writes once and then leaves the file alone."
                },
                "AppliesTo": {
                  "type": "string",
                  "description": "Restricts this template to top-level nodes of one Kind. Omit or use 'All' to match every node."
                },
                "Model": {
                  "type": "string",
                  "description": "The model file this template renders, relative to the project. Omit for 'model.json'. Use a separate model when a project has unrelated generation concerns."
                }
              },
              "required": ["Path", "OutputPattern", "Scope", "Mode"],
              "additionalProperties": false
            },
            "OverrideConfig": {
              "type": "object",
              "properties": {
                "Path": {
                  "type": "string",
                  "description": "Slash-delimited node path. '*' matches one node, '**' matches any depth: 'Products/Price', '*/CreatedAt', 'Orders/**'."
                },
                "Artifact": {
                  "type": "string",
                  "description": "Template key this rule applies to. Omit to apply it to every template. Required when Variant is set."
                },
                "Variant": {
                  "type": "string",
                  "description": "Macro variant for matched nodes: 'Currency' dispatches a Property node to the CurrencyProperty macro."
                },
                "Ignore": {
                  "type": "boolean",
                  "description": "Drops matched nodes from this artifact."
                },
                "Metadata": {
                  "type": "object",
                  "description": "Metadata merged onto matched nodes, overwriting keys the model supplied."
                }
              },
              "required": ["Path"],
              "additionalProperties": false
            },
            "LoggingConfig": {
              "type": "object",
              "properties": {
                "Enabled": { "type": "boolean" },
                "LogPath": { "type": "string" },
                "Level": {
                  "type": "string",
                  "enum": ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"]
                },
                "RollingInterval": {
                  "type": "string",
                  "enum": ["Infinite", "Year", "Month", "Day", "Hour", "Minute"]
                },
                "RetainedFileCountLimit": { "type": "integer", "minimum": 1 }
              },
              "additionalProperties": false
            }
          }
        }
        """;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "JsonSchema.Net requires reflection-based deserialization")]
    private static readonly JsonSchema Schema = JsonSerializer.Deserialize<JsonSchema>(SchemaJson)!;

    public static List<string> Validate(string json)
    {
        var errors = new List<string>();

        JsonElement instance;
        try
        {
            instance = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }).RootElement;
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON syntax error: {ex.Message}");
            return errors;
        }

        var result = Schema.Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (!result.IsValid && result.Details is not null)
        {
            foreach (var detail in result.Details)
            {
                if (detail.Errors is null || detail.Errors.Count == 0)
                    continue;

                foreach (var (keyword, message) in detail.Errors)
                {
                    var location = detail.InstanceLocation.ToString();
                    if (string.IsNullOrEmpty(location) || location == "#")
                        location = "(root)";

                    errors.Add($"Schema: {location} — {message}");
                }
            }
        }

        return errors;
    }
}
