using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Json.Schema;

namespace Pondhawk.Persistence.Core.Configuration;

public static class ProjectConfigurationSchema
{
    public const string SchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://pondhawk-mcp/persistence.project.schema.json",
          "title": "pondhawk-mcp Project Configuration",
          "description": "Configuration file for pondhawk-mcp code generation from database schemas.",
          "type": "object",
          "properties": {
            "$schema": {
              "type": "string",
              "description": "Path to the JSON Schema file for IDE support."
            },
            "ProjectName": {
              "type": "string",
              "description": "Project name used in output file names (DDL SQL, ER diagram). Defaults to 'db-design' if omitted."
            },
            "Description": {
              "type": "string",
              "description": "Optional project description included as a comment in generated DDL."
            },
            "Connection": { "$ref": "#/$defs/ConnectionConfig" },
            "OutputDir": {
              "type": "string",
              "description": "Output directory for generated files, relative to the project directory."
            },
            "Templates": {
              "type": "object",
              "description": "Named template definitions. Keys are template names.",
              "additionalProperties": { "$ref": "#/$defs/TemplateConfig" }
            },
            "Defaults": { "$ref": "#/$defs/DefaultsConfig" },
            "DataTypes": {
              "type": "object",
              "description": "Named reusable data type definitions.",
              "additionalProperties": { "$ref": "#/$defs/DataTypeConfig" }
            },
            "TypeMappings": {
              "type": "array",
              "description": "Database type to CLR/DataType mappings.",
              "items": { "$ref": "#/$defs/TypeMappingConfig" }
            },
            "Relationships": {
              "type": "array",
              "description": "Explicit foreign-key relationship definitions that supplement introspected FKs.",
              "items": { "$ref": "#/$defs/RelationshipConfig" }
            },
            "Overrides": {
              "type": "array",
              "description": "Per-class/property overrides for code generation.",
              "items": { "$ref": "#/$defs/OverrideConfig" }
            },
            "Logging": { "$ref": "#/$defs/LoggingConfig" }
          },
          "additionalProperties": false,
          "$defs": {
            "ConnectionConfig": {
              "type": "object",
              "description": "Database connection configuration.",
              "properties": {
                "Provider": {
                  "type": "string",
                  "enum": ["sqlserver", "postgresql", "mysql", "mariadb", "sqlite"],
                  "description": "Database provider."
                },
                "ConnectionString": {
                  "type": "string",
                  "description": "Database connection string. Supports ${VAR} substitution from .env."
                }
              },
              "additionalProperties": false
            },
            "TemplateConfig": {
              "type": "object",
              "description": "A Liquid template definition.",
              "properties": {
                "Path": {
                  "type": "string",
                  "description": "Relative path to the Liquid template file."
                },
                "OutputPattern": {
                  "type": "string",
                  "description": "Liquid expression for output file names."
                },
                "Scope": {
                  "type": "string",
                  "enum": ["PerModel", "SingleFile"],
                  "description": "Template scope: PerModel (one file per table) or SingleFile (one file for all)."
                },
                "Mode": {
                  "type": "string",
                  "enum": ["Always", "SkipExisting"],
                  "description": "Write mode: Always (overwrite) or SkipExisting (create only if missing)."
                },
                "AppliesTo": {
                  "type": "string",
                  "enum": ["Tables", "Views", "All"],
                  "description": "Filter which model kinds this template runs for: Tables, Views, or All (default when omitted)."
                }
              },
              "additionalProperties": false
            },
            "DefaultsConfig": {
              "type": "object",
              "description": "Default values for code generation.",
              "properties": {
                "Namespace": {
                  "type": "string",
                  "description": "Default namespace for generated code."
                },
                "ContextName": {
                  "type": "string",
                  "description": "Name of the generated database context class."
                },
                "Schema": {
                  "type": "string",
                  "description": "Default database schema name."
                },
                "IncludeViews": {
                  "type": "boolean",
                  "description": "Whether to include database views in generation."
                },
                "Include": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Table names to include (whitelist). If set, only these tables are generated."
                },
                "Exclude": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Table names to exclude (blacklist)."
                }
              },
              "additionalProperties": false
            },
            "DataTypeConfig": {
              "type": "object",
              "description": "A reusable data type definition.",
              "properties": {
                "ClrType": {
                  "type": "string",
                  "description": "CLR type name (e.g., 'decimal', 'string')."
                },
                "MaxLength": {
                  "type": "integer",
                  "description": "Maximum length constraint."
                },
                "DefaultValue": {
                  "type": "string",
                  "description": "Default value expression in generated code."
                }
              },
              "additionalProperties": false
            },
            "TypeMappingConfig": {
              "type": "object",
              "description": "Maps a database type to a CLR type or named DataType.",
              "properties": {
                "DbType": {
                  "type": "string",
                  "description": "Database type name to match."
                },
                "DataType": {
                  "type": "string",
                  "description": "Reference to a named DataType definition."
                },
                "ClrType": {
                  "type": "string",
                  "description": "Direct CLR type override."
                }
              },
              "additionalProperties": false
            },
            "RelationshipConfig": {
              "type": "object",
              "description": "An explicit foreign-key relationship definition.",
              "properties": {
                "DependentTable": {
                  "type": "string",
                  "description": "Table that holds the FK column(s)."
                },
                "DependentSchema": {
                  "type": "string",
                  "description": "Schema of the dependent table."
                },
                "DependentColumns": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Column(s) on the dependent table."
                },
                "PrincipalTable": {
                  "type": "string",
                  "description": "Table being referenced."
                },
                "PrincipalSchema": {
                  "type": "string",
                  "description": "Schema of the principal table."
                },
                "PrincipalColumns": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Column(s) on the principal table (typically the PK)."
                },
                "OnDelete": {
                  "type": "string",
                  "enum": ["Cascade", "SetNull", "NoAction"],
                  "description": "Delete behavior. Default: NoAction."
                }
              },
              "required": ["DependentTable", "DependentColumns", "PrincipalTable", "PrincipalColumns"],
              "additionalProperties": false
            },
            "OverrideConfig": {
              "type": "object",
              "description": "A per-class/property override for code generation.",
              "properties": {
                "Class": {
                  "type": "string",
                  "description": "Table name or '*' for all tables."
                },
                "Property": {
                  "type": "string",
                  "description": "Column name for property-level overrides."
                },
                "Artifact": {
                  "type": "string",
                  "description": "Template key to limit this override to."
                },
                "Variant": {
                  "type": "string",
                  "description": "Macro variant name (requires Artifact)."
                },
                "DataType": {
                  "type": "string",
                  "description": "Reference to a named DataType definition."
                },
                "Ignore": {
                  "type": "boolean",
                  "description": "Set to true to exclude this property from output."
                }
              },
              "additionalProperties": false
            },
            "LoggingConfig": {
              "type": "object",
              "description": "File-based logging configuration.",
              "properties": {
                "Enabled": {
                  "type": "boolean",
                  "description": "Whether logging is enabled."
                },
                "LogPath": {
                  "type": "string",
                  "description": "Path to the log file."
                },
                "Level": {
                  "type": "string",
                  "enum": ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
                  "description": "Minimum log level."
                },
                "RollingInterval": {
                  "type": "string",
                  "enum": ["Infinite", "Year", "Month", "Day", "Hour", "Minute"],
                  "description": "How often to roll the log file."
                },
                "RetainedFileCountLimit": {
                  "type": "integer",
                  "description": "Maximum number of log files to retain."
                }
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
