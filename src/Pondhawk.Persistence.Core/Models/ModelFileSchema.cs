using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Json.Schema;

namespace Pondhawk.Persistence.Core.Models;

public static class ModelFileSchema
{
    public const string SchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://pondhawk-mcp/model.schema.json",
          "title": "pondhawk Input Model",
          "description": "The things to generate. Nodes nest arbitrarily; anything beyond Name, Kind and Children is metadata and is reachable from templates as a direct member.",
          "type": "object",
          "properties": {
            "$schema": {
              "type": "string",
              "description": "Path to this JSON Schema file, for IDE support."
            },
            "Name": {
              "type": "string",
              "description": "Name of the model as a whole, available to templates as {{ model.Name }}."
            },
            "Nodes": {
              "type": "array",
              "description": "Top-level nodes. A PerItem template renders once per node listed here.",
              "items": { "$ref": "#/$defs/Node" }
            }
          },
          "required": ["Nodes"],
          "additionalProperties": true,
          "$defs": {
            "Node": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "minLength": 1,
                  "description": "Node name. Forms part of the node's override path."
                },
                "Kind": {
                  "type": "string",
                  "minLength": 1,
                  "description": "What this node is — Class, Property, Endpoint, Parameter. Selects the macro that renders it: a Kind of 'Property' dispatches to DefaultProperty."
                },
                "Children": {
                  "type": "array",
                  "description": "Nested nodes, to any depth.",
                  "items": { "$ref": "#/$defs/Node" }
                }
              },
              "required": ["Name", "Kind"],
              "additionalProperties": true
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

        var result = Schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!result.IsValid && result.Details is not null)
        {
            foreach (var detail in result.Details)
            {
                if (detail.Errors is null || detail.Errors.Count == 0)
                    continue;

                foreach (var (_, message) in detail.Errors)
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
