using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Pondhawk.Persistence.Core.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Pondhawk.Persistence.Mcp.Tools;

[McpServerToolType]
public sealed class InitTool
{
    [McpServerTool(Name = "init"), Description("Creates a skeleton persistence.project.json, starter Liquid templates, a .env file for database credentials, and an AGENTS.md file in the project directory. See AGENTS.md for detailed usage instructions.")]
    public static string Execute(
        ServerContext ctx,
        [Description("Database provider for the default connection (sqlserver, postgresql, mysql, mariadb, sqlite). Default: sqlserver")]
        string provider = "sqlserver",
        [Description("Default namespace for generated code. Default: MyApp.Data")]
        string @namespace = "MyApp.Data",
        [Description("Database connection string. If provided, written to .env so introspect_schema works immediately. If omitted, .env is created with a placeholder.")]
        string? connectionString = null)
    {
        var (logger, sw) = ctx.StartToolCall("init", $"provider={provider}, namespace={@namespace}");
        var configPath = ctx.ConfigPath;

        if (File.Exists(configPath))
        {
            logger.LogError("Tool init failed — persistence.project.json already exists");
            throw new InvalidOperationException("persistence.project.json already exists. Use validate_config to check the existing configuration.");
        }

        // Build skeleton config
        var contextName = @namespace.Split('.').First();
        var config = new ProjectConfiguration
        {
            Schema_ = "./persistence.project.schema.json",
            Connection = new ConnectionConfig { Provider = provider, ConnectionString = "${DB_CONNECTION}" },
            OutputDir = "src/Data",
            Templates = new Dictionary<string, TemplateConfig>
            {
                ["entity"] = new()
                {
                    Path = "templates/entity.generated.liquid",
                    OutputPattern = "Entities/{{entity.Name | pascal_case}}.generated.cs",
                    Scope = "PerModel",
                    Mode = "Always"
                },
                ["entity-stub"] = new()
                {
                    Path = "templates/entity.stub.liquid",
                    OutputPattern = "Entities/{{entity.Name | pascal_case}}.cs",
                    Scope = "PerModel",
                    Mode = "SkipExisting"
                }
            },
            Defaults = new DefaultsConfig
            {
                Namespace = @namespace,
                ContextName = contextName,
                Schema = "dbo",
                IncludeViews = false
            },
            Logging = new LoggingConfig { Enabled = false }
        };

        // Write config
        ProjectConfigurationLoader.Save(configPath, config);

        // Write JSON Schema files for IDE support and validation
        var schemaFilePath = Path.Combine(ctx.ProjectDir, "persistence.project.schema.json");
        File.WriteAllText(schemaFilePath, ProjectConfigurationSchema.SchemaJson, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "db-design.schema.json"), DbDesignFileSchema.SchemaJson, new UTF8Encoding(false));

        // Create templates directory
        var templatesDir = Path.Combine(ctx.ProjectDir, "templates");
        Directory.CreateDirectory(templatesDir);

        var utf8NoBom = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(templatesDir, "entity.generated.liquid"), GetEntityTemplate(), utf8NoBom);
        File.WriteAllText(Path.Combine(templatesDir, "entity.stub.liquid"), GetStubTemplate(@namespace), utf8NoBom);

        // Write AGENTS.md
        File.WriteAllText(Path.Combine(ctx.ProjectDir, "AGENTS.md"), GetAgentsMarkdown(), utf8NoBom);

        // Write .env with connection string (real or placeholder)
        var envPath = Path.Combine(ctx.ProjectDir, ".env");
        if (!File.Exists(envPath))
        {
            var envContent = string.IsNullOrWhiteSpace(connectionString)
                ? GetEnvFile(provider)
                : $"# Database connection string — keep this file out of version control\nDB_CONNECTION={connectionString}\n";
            File.WriteAllText(envPath, envContent, utf8NoBom);
        }

        var filesCreated = new[]
        {
            "persistence.project.json",
            "persistence.project.schema.json",
            "db-design.schema.json",
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
            NextSteps = "Read AGENTS.md for full usage instructions. Update the connection string in .env and run introspect_schema to verify connectivity."
        });
    }

    private static string GetEntityTemplate() => """
        // <auto-generated>
        // This file was generated by pondhawk-mcp. Do not edit manually.
        // Any changes will be overwritten on next generation.
        // </auto-generated>

        namespace {{ config.Defaults.Namespace }}.Entities;

        {%- macro DefaultClass(m) %}
        public partial class {{ m.Name | pascal_case }}
        {%- endmacro %}

        {% dispatch entity %}
        {

        {%- macro DefaultProperty(a) %}
            public {{ a.ClrType | type_nullable: a.IsNullable }} {{ a.Name | pascal_case }} { get; set; }{% if a.ClrType == "string" and a.IsNullable == false %} = null!;{% endif %}
        {%- endmacro %}

        {%- for a in entity.Attributes %}
        {% dispatch a %}
        {%- endfor %}

        {%- for fk in entity.ForeignKeys %}
            public virtual {{ fk.PrincipalTable | pascal_case | singularize }} {{ fk.PrincipalTable | pascal_case | singularize }} { get; set; } = null!;
        {%- endfor %}

        {%- for ref in entity.ReferencingForeignKeys %}
            public virtual ICollection<{{ ref.Table | pascal_case | singularize }}> {{ ref.Table | pascal_case }} { get; set; } = new List<{{ ref.Table | pascal_case | singularize }}>();
        {%- endfor %}
        }
        """;

    private static string GetStubTemplate(string ns) =>
        "namespace " + ns + ".Entities;\n\npublic partial class {{ entity.Name | pascal_case }}\n{\n}\n";

    private static string GetEnvFile(string provider) => provider.ToLowerInvariant() switch
    {
        "sqlite" => """
            # Database connection string — keep this file out of version control
            DB_CONNECTION=Data Source=./mydb.db
            """,
        "postgresql" => """
            # Database connection string — keep this file out of version control
            DB_CONNECTION=Host=localhost;Database=MyDatabase;Username=postgres;Password=changeme
            """,
        "mysql" or "mariadb" => """
            # Database connection string — keep this file out of version control
            DB_CONNECTION=Server=localhost;Database=MyDatabase;User=root;Password=changeme
            """,
        _ => """
            # Database connection string — keep this file out of version control
            DB_CONNECTION=Server=localhost;Database=MyDatabase;Trusted_Connection=true;
            """
    };

    internal static string GetAgentsMarkdown() => """
        # pondhawk-mcp — AI Agent Instructions

        This project uses **pondhawk-mcp** to generate code from database schemas using Liquid templates.

        ## Available MCP Tools

        1. **init** — Create skeleton project configuration and templates
        2. **introspect_schema** — Introspect the database schema, write `db-design.json`, and return a summary
        3. **generate** — Generate code by rendering templates against `db-design.json` data, merged with explicit `Relationships` from config
        4. **generate_ddl** — Generate dialect-specific DDL SQL from `db-design.json` (sqlserver, postgresql, mysql, sqlite)
        5. **generate_diagram** — Generate an interactive HTML ER diagram from `db-design.json`
        6. **generate_migration** — Generate a versioned delta migration SQL script by diffing `db-design.json` against the last snapshot
        7. **list_templates** — List available templates
        8. **validate_config** — Validate the project configuration
        9. **update** — Updates AGENTS.md, JSON Schema, and normalizes config after upgrading pondhawk-mcp

        ## Workflow

        1. Run `introspect_schema` to discover tables, columns, and keys — this writes `db-design.json`
        2. Review the schema output and customize `persistence.project.json` as needed:
           - Add `Relationships` for explicit FK definitions (essential for databases without FK constraints)
           - Add `DataTypes` for custom type mappings
           - Add `Overrides` for per-class/property variants
           - Adjust `TypeMappings` for database-to-CLR type overrides
        3. Run `generate` to render templates and write generated files
        4. **Always run `validate_config`** after editing the configuration to catch errors early

        ## Configuration File

        `persistence.project.json` is the single source of truth. It includes a `$schema` reference to `persistence.project.schema.json` for IDE autocompletion and validation. The schema enforces `additionalProperties: false` on all objects, so misspelled or unknown field names will be caught immediately by `validate_config`.

        It contains:
        - **Connection** — Database connection with provider and connection string
        - **Templates** — Liquid template definitions with output patterns and modes
        - **Defaults** — Default namespace, schema, include/exclude filters, includeViews
        - **DataTypes** — Custom reusable type definitions (ClrType, MaxLength, DefaultValue)
        - **TypeMappings** — Database type to CLR/DataType mappings (auto-populated on introspection). Uses family-based inference: `decimal(42,2)` resolves via the base type `decimal` when no exact match exists
        - **Overrides** — Per-class/property variant, DataType, and Ignore assignments
        - **Relationships** — Explicit FK definitions that supplement introspected FKs (see Relationships section below)
        - **Logging** — Optional file-based logging configuration

        ## Schema File

        `db-design.json` is written by the `introspect_schema` tool or authored by hand for design-first use.

        - Contains tables, views, columns, primary keys, introspected foreign keys, and indexes
        - Does **not** contain explicit `Relationships` from `persistence.project.json` — those are merged at generation time
        - Read this file when you need schema details (column names, types, etc.) instead of re-introspecting
        - This file allows `generate` to work without a live database connection

        ## Relationships

        The `Relationships` array in `persistence.project.json` defines explicit foreign-key relationships that supplement what the database introspection discovers. This is **essential** for databases that do not use FK constraints.

        Explicit relationships are merged with introspected FKs at generation time — they are **not** written to `db-design.json`. Changes to `Relationships` take effect on the next `generate` call without re-running `introspect_schema`.

        Each entry has these **exact** field names:

        | Field | Type | Required | Description |
        |-------|------|----------|-------------|
        | `DependentTable` | `string` | Yes | Table that holds the FK column(s) |
        | `DependentSchema` | `string` | No | Schema of the dependent table (defaults to `Defaults.Schema`) |
        | `DependentColumns` | `string[]` | Yes | Column(s) on the dependent table (always an **array**) |
        | `PrincipalTable` | `string` | Yes | Table being referenced |
        | `PrincipalSchema` | `string` | No | Schema of the principal table (defaults to `Defaults.Schema`) |
        | `PrincipalColumns` | `string[]` | Yes | Column(s) on the principal table, typically the PK (always an **array**) |
        | `OnDelete` | `string` | No | Delete behavior: `Cascade`, `SetNull`, or `NoAction` (default: `NoAction`) |

        **Example:**
        ```json
        "Relationships": [
          {
            "DependentTable": "OrderItems",
            "DependentColumns": ["OrderId"],
            "PrincipalTable": "Orders",
            "PrincipalColumns": ["Id"]
          },
          {
            "DependentTable": "OrderItems",
            "DependentColumns": ["ProductId"],
            "PrincipalTable": "Products",
            "PrincipalColumns": ["Id"],
            "OnDelete": "Cascade"
          }
        ]
        ```

        **Important:** Table names must exactly match the names in `db-design.json` (case-insensitive). `DependentColumns` and `PrincipalColumns` are **always arrays**, even for single-column FKs.

        In templates, these relationships appear as:
        - `entity.ForeignKeys` — on the dependent table (e.g., OrderItems gets FK entries pointing to Orders and Products)
        - `entity.ReferencingForeignKeys` — on the principal table (e.g., Orders gets a referencing entry from OrderItems)

        ## Environment Variables

        Connection strings support `${VAR}` substitution to keep database credentials out of version control:
        - The `.env` file in the project directory contains `KEY=VALUE` pairs (created by `init`)
        - System environment variables override `.env` values
        - Example: `"ConnectionString": "${DB_CONNECTION}"`
        - Only connection strings are resolved — all other configuration belongs in `persistence.project.json`
        - Add `.env` to `.gitignore` to keep credentials out of source control

        ## Override Rules

        Overrides in the `Overrides` array are matched using specificity rules:
        - **Class**: Exact table name or `*` (wildcard for all tables). Exact beats wildcard.
        - **Property**: Exact column name (no wildcards). Omit for class-level overrides.
        - **Artifact**: Limits the override to a specific template key. Omit to apply to all.
        - **Variant**: Names the macro variant (requires `Artifact`). Resolved by `{% dispatch %}`.
        - **DataType**: References a named entry in `DataTypes` to override ClrType/MaxLength/DefaultValue.
        - **Ignore**: Set to `true` to exclude a property from generated output.
        - When multiple overrides match, **last entry wins** among equally specific matches.

        ## Custom Data Types

        Define reusable type definitions in `DataTypes`:
        ```json
        "DataTypes": {
          "Money": { "ClrType": "decimal", "DefaultValue": "0m" },
          "ShortString": { "ClrType": "string", "MaxLength": 100 }
        }
        ```
        Reference them in `TypeMappings` (by database type) or `Overrides` (by class/property).

        ## Template System

        Templates use the Fluid (Liquid) engine with custom tags and filters:
        - `{% macro Name(param) %}...{% endmacro %}` — Define callable macros
        - `{% dispatch object %}` — Call the appropriate macro variant for a Model or Attribute
        - Custom filters: `pascal_case`, `camel_case`, `snake_case`, `pluralize`, `singularize`, `type_nullable`
        - Strict mode is enabled: undefined variables and filters will produce errors

        ### Creating New Templates

        1. Create a `.liquid` file in the `templates/` directory
        2. Add a template entry in `persistence.project.json` under `Templates`:
           - **Path**: Relative path to the template file
           - **OutputPattern**: Liquid expression for output file names (e.g., `{{ entity.Name | pascal_case }}.cs`)
           - **Scope**: `PerModel` (one file per table/view) or `SingleFile` (one file for all)
           - **Mode**: `Always` (overwrite) or `SkipExisting` (create only if missing)
           - **AppliesTo** *(optional)*: `Tables`, `Views`, or `All` (default). Limits which model kinds a template runs for
        3. PerModel templates receive: `entity`, `schema`, `database`, `config`, `parameters`
        4. SingleFile templates receive: `entities`, `views`, `schemas`, `database`, `config`, `parameters`
        5. Templates that render to whitespace-only output are automatically skipped — no file is written to disk

        ### Navigation Properties in Templates

        Templates generate navigation properties by iterating `entity.ForeignKeys` and `entity.ReferencingForeignKeys`:

        ```liquid
        {%- for fk in entity.ForeignKeys %}
            public virtual {{ fk.PrincipalTable | pascal_case | singularize }} {{ fk.PrincipalTable | pascal_case | singularize }} { get; set; } = null!;
        {%- endfor %}

        {%- for ref in entity.ReferencingForeignKeys %}
            public virtual ICollection<{{ ref.Table | pascal_case | singularize }}> {{ ref.Table | pascal_case }} { get; set; } = new List<{{ ref.Table | pascal_case | singularize }}>();
        {%- endfor %}
        ```

        These collections include both introspected FKs from `db-design.json` and explicit `Relationships` from config, merged at generation time.

        ## Migrations Workflow

        pondhawk-mcp can generate **versioned delta migration scripts** by diffing `db-design.json` against the last snapshot. This eliminates hand-writing ALTER statements.

        ### How It Works

        1. Edit `db-design.json` to reflect the desired schema (add/remove/modify tables, columns, indexes, FKs)
        2. Run `generate_migration` with a short description
        3. The tool diffs against the last snapshot to produce:
           - `V{NNN}__{slug}.sql` — delta SQL with ALTER/CREATE/DROP statements
           - `V{NNN}__{slug}.json` — snapshot of `db-design.json` at this version
        4. Review the generated SQL
        5. Commit both files alongside `db-design.json`
        6. Deploy with your migration runner (DbUp, Flyway, etc.)

        ### Parameters

        | Parameter | Required | Description |
        |-----------|----------|-------------|
        | `description` | Yes | Short description (e.g., "add orders table"). Slugified for the filename. |
        | `provider` | No | Target dialect: `sqlserver`, `postgresql`, `mysql`, `sqlite`. Defaults to provider from `persistence.project.json`. |
        | `output` | No | Output directory relative to project root. Default: `migrations` |
        | `dryRun` | No | If true, compute diff and generate SQL but do not write files. Default: false |

        ### First Migration (Bootstrap)

        If no snapshots exist, the baseline is an empty schema. All tables produce `TableAdded` changes, equivalent to `generate_ddl` output but in migration format:

        ```
        generate_migration description="initial schema" provider="sqlserver"
        → migrations/V001__initial_schema.sql   (CREATE TABLE statements)
        → migrations/V001__initial_schema.json  (snapshot)
        ```

        ### Subsequent Migrations

        Edit `db-design.json` (add a column, add a table, etc.) then generate:

        ```
        generate_migration description="add display name to users"
        → migrations/V002__add_display_name_to_users.sql   (ALTER TABLE ADD COLUMN)
        → migrations/V002__add_display_name_to_users.json  (updated snapshot)
        ```

        ### Detected Changes

        The differ detects: table add/remove, column add/remove/modify, index add/remove/modify, foreign key add/remove/modify, and primary key modifications.

        ### Warnings

        The tool emits warnings for risky operations:
        - **Destructive** — table or column removed (data loss risk)
        - **PossibleRename** — column removed + similar column added (may be a rename)
        - **DataLoss** — column type narrowed (e.g., `varchar(255)` → `varchar(50)`)
        - **NoChanges** — baseline and desired are identical; no migration written

        ### Dry Run

        Use `dryRun=true` to preview changes without writing files. The response includes the generated SQL and change list.

        ### Migration Directory Structure

        ```
        migrations/
          V001__initial_schema.sql
          V001__initial_schema.json
          V002__add_orders_table.sql
          V002__add_orders_table.json
        ```

        Each `.sql` has a paired `.json` snapshot. The tool validates history on each run — orphaned files (`.sql` without `.json` or vice versa) cause an error.

        ## Updating After Upgrade

        When pondhawk-mcp is upgraded, existing projects retain stale copies of `AGENTS.md` and `persistence.project.schema.json`. This means Claude won't learn about new features (like `AppliesTo` filtering) and IDE autocompletion may be outdated. The `init` tool refuses to overwrite an existing project.

        Run the `update` tool to refresh these files:

        - **AGENTS.md** — Overwritten with the latest embedded instructions so Claude knows about all current features
        - **persistence.project.schema.json** — Overwritten with the latest JSON Schema so IDE autocompletion and `validate_config` recognize new properties
        - **persistence.project.json** — Normalized via a load/save round-trip, which picks up new default values and drops any properties removed in the new version

        All existing configuration values (Templates, TypeMappings, Overrides, Relationships, DataTypes, etc.) are preserved during normalization. The tool requires `persistence.project.json` to exist — if it doesn't, run `init` first.

        ## Variant System

        The variant system controls per-class and per-property code generation:
        1. Define overrides in `persistence.project.json` with `Variant` and `Artifact` names
        2. Define matching macro functions in templates (e.g., `SoftDeleteClass`, `AuditTimestampProperty`)
        3. The `{% dispatch %}` tag resolves the correct macro based on the variant
        4. If a variant macro is missing, falls back to `DefaultClass` / `DefaultProperty`
        5. If no macro is found at all, an error comment is written to the output

        ## Design-First Pipeline

        pondhawk-mcp supports a **design-first** workflow where you author `db-design.json` by hand to design a new database schema, then generate DDL SQL and ER diagrams.

        ### Origin Field

        Always set `"Origin": "design"` when hand-authoring `db-design.json`. This prevents `introspect_schema` from accidentally overwriting your design. To switch back to database-first, delete `db-design.json` or change `Origin` to `"introspected"`.

        ### Design-First db-design.json Example

        ```json
        {
          "$schema": "db-design.schema.json",
          "Origin": "design",
          "Schemas": [
            {
              "Name": "public",
              "Tables": [
                {
                  "Name": "Users",
                  "Note": "Application users",
                  "Columns": [
                    { "Name": "Id", "DataType": "int", "IsPrimaryKey": true, "IsIdentity": true },
                    { "Name": "Email", "DataType": "varchar(255)", "Note": "Unique email address" },
                    { "Name": "Status", "DataType": "varchar(20)", "Note": "References UserStatus enum" }
                  ],
                  "PrimaryKey": { "Columns": ["Id"] },
                  "Indexes": [
                    { "Columns": ["Email"], "IsUnique": true }
                  ]
                }
              ]
            }
          ],
          "Enums": [
            {
              "Name": "UserStatus",
              "Note": "User account lifecycle",
              "Values": [
                { "Name": "Active" },
                { "Name": "Suspended" },
                { "Name": "Deleted" }
              ]
            }
          ]
        }
        ```

        ### Required vs Optional Fields

        | Field | Required | Notes |
        |-------|----------|-------|
        | `Origin` | Yes | `"design"` for hand-authored schemas |
        | Table `Name` | Yes | Table name |
        | Table `Columns` | Yes | At least one column |
        | Column `Name` | Yes | Column name |
        | Column `DataType` | Yes | Database type (e.g., `int`, `varchar(255)`) |
        | `ClrType` | No | Only needed for C# code generation |
        | `ReferencingForeignKeys` | No | Computed from ForeignKeys |
        | `Provider`, `Database` | No | Not needed for DDL generation |
        | Constraint `Name` | No | Auto-generated if omitted |

        ### Workflow Examples

        **Design a new database:**
        1. Write `db-design.json` with `"Origin": "design"` — define tables, columns, FKs, enums
        2. Run `generate_ddl` with your target provider (e.g., `provider=postgresql`) to generate SQL
        3. Review the generated SQL file
        4. Deploy to your database

        **Visualize an existing database:**
        1. Run `introspect_schema` to generate `db-design.json` from a live database
        2. Run `generate_diagram` to create an interactive HTML ER diagram
        3. Open the HTML file in a browser

        **Design and visualize:**
        1. Write `db-design.json` with `"Origin": "design"`
        2. Run `generate_diagram` to review the schema visually
        3. Run `generate_ddl` to generate deployment SQL
        """;
}
