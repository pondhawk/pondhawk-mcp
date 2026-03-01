using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Json.Schema;

namespace Pondhawk.Persistence.Core.Configuration;

public static class DbDesignFileSchema
{
    public const string SchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://pondhawk-mcp/db-design.schema.json",
          "title": "pondhawk-mcp Database Design Schema",
          "description": "Schema file for pondhawk-mcp database design and introspection.",
          "type": "object",
          "properties": {
            "$schema": {
              "type": "string",
              "description": "Path to the JSON Schema file for IDE support."
            },
            "Origin": {
              "type": "string",
              "enum": ["introspected", "design"],
              "description": "How this file was created: 'introspected' from a live database, or 'design' for hand-authored schemas."
            },
            "Database": {
              "type": "string",
              "description": "Database name."
            },
            "Provider": {
              "type": "string",
              "description": "Database provider (sqlserver, postgresql, mysql, mariadb, sqlite)."
            },
            "Schemas": {
              "type": "array",
              "description": "Schema groups containing tables and views.",
              "items": { "$ref": "#/$defs/SchemaGroup" }
            },
            "Enums": {
              "type": "array",
              "description": "Enum type definitions for use in DDL generation.",
              "items": { "$ref": "#/$defs/EnumDef" }
            }
          },
          "required": ["Origin"],
          "additionalProperties": false,
          "$defs": {
            "SchemaGroup": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Schema name (e.g., 'dbo', 'public')."
                },
                "Tables": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/TableDef" }
                },
                "Views": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/TableDef" }
                }
              },
              "additionalProperties": false
            },
            "TableDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Table or view name."
                },
                "Schema": {
                  "type": "string",
                  "description": "Schema name for this table."
                },
                "Note": {
                  "type": "string",
                  "description": "Descriptive note, emitted as SQL comment in DDL."
                },
                "Columns": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/ColumnDef" }
                },
                "PrimaryKey": { "$ref": "#/$defs/PrimaryKeyDef" },
                "ForeignKeys": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/ForeignKeyDef" }
                },
                "Indexes": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/IndexDef" }
                },
                "ReferencingForeignKeys": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/ReferencingForeignKeyDef" }
                }
              },
              "required": ["Name", "Columns"],
              "additionalProperties": false
            },
            "ColumnDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Column name."
                },
                "DataType": {
                  "type": "string",
                  "description": "Database data type (e.g., 'int', 'varchar(255)')."
                },
                "ClrType": {
                  "type": "string",
                  "description": "CLR type name. Optional for design-first schemas."
                },
                "Note": {
                  "type": "string",
                  "description": "Descriptive note, emitted as SQL comment in DDL."
                },
                "IsNullable": {
                  "type": "boolean",
                  "description": "Whether the column allows NULL values."
                },
                "IsPrimaryKey": {
                  "type": "boolean",
                  "description": "Whether the column is part of the primary key."
                },
                "IsIdentity": {
                  "type": "boolean",
                  "description": "Whether the column is auto-incrementing."
                },
                "MaxLength": {
                  "type": "integer",
                  "description": "Maximum length constraint."
                },
                "Precision": {
                  "type": "integer",
                  "description": "Numeric precision."
                },
                "Scale": {
                  "type": "integer",
                  "description": "Numeric scale."
                },
                "DefaultValue": {
                  "type": "string",
                  "description": "Default value expression."
                }
              },
              "required": ["Name", "DataType"],
              "additionalProperties": false
            },
            "PrimaryKeyDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Constraint name. Auto-generated if omitted."
                },
                "Columns": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Column(s) in the primary key."
                }
              },
              "additionalProperties": false
            },
            "ForeignKeyDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Constraint name. Auto-generated if omitted."
                },
                "Columns": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Column(s) on this table."
                },
                "PrincipalTable": {
                  "type": "string",
                  "description": "Referenced table name."
                },
                "PrincipalSchema": {
                  "type": "string",
                  "description": "Schema of the referenced table."
                },
                "PrincipalColumns": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Referenced column(s)."
                },
                "OnDelete": {
                  "type": "string",
                  "enum": ["Cascade", "SetNull", "SetDefault", "NoAction", "Restrict"],
                  "description": "Delete behavior."
                },
                "OnUpdate": {
                  "type": "string",
                  "enum": ["Cascade", "SetNull", "SetDefault", "NoAction", "Restrict"],
                  "description": "Update behavior."
                }
              },
              "additionalProperties": false
            },
            "IndexDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Index name. Auto-generated if omitted."
                },
                "Columns": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Column(s) in the index."
                },
                "IsUnique": {
                  "type": "boolean",
                  "description": "Whether the index enforces uniqueness."
                }
              },
              "additionalProperties": false
            },
            "ReferencingForeignKeyDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string"
                },
                "Table": {
                  "type": "string",
                  "description": "Table that references this table."
                },
                "Schema": {
                  "type": "string"
                },
                "Columns": {
                  "type": "array",
                  "items": { "type": "string" }
                },
                "PrincipalColumns": {
                  "type": "array",
                  "items": { "type": "string" }
                }
              },
              "additionalProperties": false
            },
            "EnumDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Enum type name."
                },
                "Note": {
                  "type": "string",
                  "description": "Descriptive note."
                },
                "Values": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/EnumValueDef" },
                  "description": "Ordered list of enum values."
                }
              },
              "required": ["Name", "Values"],
              "additionalProperties": false
            },
            "EnumValueDef": {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string",
                  "description": "Enum value name."
                },
                "Note": {
                  "type": "string",
                  "description": "Descriptive note for this value."
                }
              },
              "required": ["Name"],
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
