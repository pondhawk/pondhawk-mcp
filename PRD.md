# PRD: pondhawk-mcp

## 1. Overview

**pondhawk-mcp** is an MCP (Model Context Protocol) server built with C# on .NET 10 that enables AI agents to introspect relational database schemas and generate code artifacts — primarily EF Core entities and related classes — using customizable Liquid templates. It bridges the gap between existing database schemas and modern .NET code by letting developers define their own generation templates as project assets, committed alongside application code.

Liquid templates are primarily authored and maintained by AI agents (e.g., Claude) as part of the development workflow, though developers can also edit them manually when needed.

### Problem Statement

.NET developers working with database-first workflows spend significant time manually creating and maintaining EF Core entity classes, DTOs, mappings, and related boilerplate from existing database schemas. Existing scaffolding tools (e.g., `dotnet ef dbcontext scaffold`) are rigid, produce opinionated output, and don't integrate with AI-assisted development workflows.

### Solution

pondhawk-mcp exposes database schema introspection and template-driven code generation as MCP tools, allowing AI agents (Claude, Copilot, etc.) to:

1. Discover and introspect database schemas via a pre-configured connection
2. Author and refine Liquid templates to control exactly what code is generated
3. Generate fully customizable code by rendering those templates against schema metadata
4. Write generated files directly to the file system using a partial class pattern that cleanly separates generated code from developer-authored code

### Target Audience

- **.NET developers** using AI-assisted coding tools who need to generate data-layer code from existing databases
- **Enterprise development teams** with established database schemas who want repeatable, template-driven code generation integrated into their AI workflows

---

## 2. Goals and Non-Goals

### Goals

- Provide MCP tools for database schema introspection across multiple RDBMS
- Generate code artifacts from Liquid templates with full customization
- Write generated files directly to the file system
- Use C# partial classes to separate generated code from hand-written customizations
- Support a single database connection per project defined in the project configuration file
- Write introspected schema to a version-controlled file (`db-design.json`) so generation works without a live database connection
- Support table and view filtering via include/exclude patterns
- Keep templates, schema, and project configuration as version-controlled project assets
- Support design-first schema authoring: generate dialect-specific DDL SQL from `db-design.json` for deploying new databases
- Generate interactive HTML ER diagrams from `db-design.json` for visual schema review

### Non-Goals

- Providing a UI or CLI beyond the MCP server interface
- Runtime ORM functionality or database migration support (note: one-shot DDL generation for initial schema deployment is a goal; incremental migration scripts are not)
- Supporting non-relational databases
- Template authoring tools or visual template editors

---

## 3. Technical Architecture

### Technology Stack

| Component            | Technology                              |
| -------------------- | --------------------------------------- |
| Runtime              | .NET 10 (released November 2025)        |
| Language             | C# 13                                   |
| MCP SDK              | ModelContextProtocol (latest stable)    |
| Template Engine      | Fluid (latest stable)                   |
| DB Introspection     | DatabaseSchemaReader (latest stable)    |
| Configuration        | System.Text.Json                        |
| Logging              | Serilog (latest stable) + Serilog.Sinks.File (latest stable) + Serilog.Extensions.Logging (latest stable) |
| Build System         | Cake Frosting (latest stable)           |
| Transport            | stdio (standard MCP transport)          |

All NuGet packages must use the latest stable (non-preview) release as of February 2026. Do not use preview, beta, or release-candidate packages.

### Supported Databases

| ADO.NET Provider                          | Databases Supported                                                    |
| ----------------------------------------- | ---------------------------------------------------------------------- |
| Microsoft.Data.SqlClient (latest stable)  | SQL Server (2012+), Azure SQL Database, Azure SQL Managed Instance     |
| Npgsql (latest stable)                    | PostgreSQL (10+), AWS Aurora PostgreSQL, Azure Database for PostgreSQL  |
| MySqlConnector (latest stable)            | MySQL (5.7+), MariaDB (10.2+), AWS Aurora MySQL, Azure Database for MySQL |
| Microsoft.Data.Sqlite (latest stable)     | SQLite 3                                                               |

All four providers are bundled as dependencies of `Pondhawk.Persistence.Core`.

**DDL generation uses FluentMigrator v8.0.1** (`FluentMigrator.Runner.SqlServer`, `.Postgres`, `.MySql`, `.SQLite`) as a dialect-aware SQL formatting engine. The generators are used standalone (no DI, no database connection) to produce correctly-quoted, dialect-specific DDL from in-memory expression objects. The HTML ER diagram generator is implemented with no external libraries beyond the existing stack. The `db-design.json` format is extended with optional fields (enums, notes, OnUpdate) to support design-first authoring — see section 16.2 for details.

### High-Level Architecture

```
┌─────────────┐       stdio        ┌──────────────────────────────┐
│  AI Agent   │◄───────────────────►│        pondhawk-mcp           │
│ (Claude,    │   MCP Protocol      │                              │
│  Copilot)   │                     │  ┌────────────────────────┐  │
└─────────────┘                     │  │     MCP Tool Layer     │  │
                                    │  └───────────┬────────────┘  │
                                    │              │               │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   Schema Introspection │  │
                                    │  │       Engine           │  │
                                    │  └───────────┬────────────┘  │
                                    │              │               │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   Template Rendering   │  │
                                    │  │     Engine (Fluid)     │  │
                                    │  └───────────┬────────────┘  │
                                    │              │               │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   File Writer          │  │
                                    │  └────────────────────────┘  │
                                    └──────────────┬───────────────┘
                                                   │
                                    ┌──────────────┴───────────────┐
                                    │     Target Databases         │
                                    │  SQL Server │ PostgreSQL     │
                                    │  MySQL      │ SQLite         │
                                    └──────────────────────────────┘
```

### Solution Structure

The solution is split into two projects to separate core functionality from the MCP transport layer. This allows the core library to be reused by other modalities (e.g., a CLI tool) without depending on MCP.

```
pondhawk-mcp.slnx
├── src/
│   ├── Pondhawk.Persistence.Core/            ← class library (.NET 10)
│   │   ├── Configuration/
│   │   │   ├── ProjectConfiguration.cs       (persistence.project.json model + loader)
│   │   │   ├── DbDesignFileSchema.cs           (embedded JSON Schema for db-design.json validation)
│   │   │   ├── EnvironmentResolver.cs        (.env loading + ${VAR} substitution)
│   │   │   └── ConfigurationValidator.cs     (validate_config logic)
│   │   ├── Introspection/
│   │   │   ├── SchemaIntrospector.cs         (DatabaseSchemaReader wrapper)
│   │   │   ├── RelationshipMerger.cs         (merge explicit Relationships with introspected FKs at generation time)
│   │   │   └── TypeMapper.cs                (built-in + project-level type mapping)
│   │   ├── Models/
│   │   │   ├── Model.cs                      (table/view → Model with GetVariant)
│   │   │   ├── Attribute.cs                  (column → Attribute with GetVariant)
│   │   │   └── OverrideResolver.cs           (specificity rules, Ignore filtering)
│   │   ├── Rendering/
│   │   │   ├── TemplateEngine.cs             (Fluid setup, custom filters, compile + cache)
│   │   │   ├── DispatchTag.cs                (custom {% dispatch %} tag)
│   │   │   ├── MacroTag.cs                   (custom {% macro %} tag)
│   │   │   └── FileWriter.cs                (output path resolution, encoding, write logic)
│   │   ├── Ddl/                              ← DDL generation (see section 16)
│   │   │   ├── IDdlGenerator.cs
│   │   │   ├── DdlGeneratorFactory.cs
│   │   │   ├── SqlServerDdlGenerator.cs
│   │   │   ├── PostgreSqlDdlGenerator.cs
│   │   │   ├── MySqlDdlGenerator.cs
│   │   │   ├── SqliteDdlGenerator.cs
│   │   │   └── DdlTypeMapper.cs             (generic-to-dialect type mappings)
│   │   ├── Diagrams/                         ← ER diagram generation (see section 16)
│   │   │   └── DiagramGenerator.cs           (HTML ER diagram generator)
│   │   ├── Logging/
│   │   │   └── LoggingService.cs             (Serilog setup, MEL integration, config-driven init)
│   │   └── Caching/
│   │       └── TimestampCache.cs             (file timestamp invalidation)
│   │
│   └── Pondhawk.Persistence.Mcp/             ← MCP server (thin layer)
│       ├── Program.cs                        (--project arg, stdio transport, Serilog/MEL wiring, server setup)
│       └── Tools/
│           ├── InitTool.cs
│           ├── IntrospectSchemaTool.cs
│           ├── GenerateTool.cs
│           ├── GenerateDdlTool.cs            ← new (see section 16)
│           ├── GenerateDiagramTool.cs        ← new (see section 16)
│           ├── ListTemplatesTool.cs
│           ├── ValidateConfigTool.cs
│           └── UpdateTool.cs
│
├── tests/
│   ├── Pondhawk.Persistence.Core.Tests/      ← unit tests (xUnit)
│   │   ├── Configuration/
│   │   │   ├── ProjectConfigurationTests.cs
│   │   │   ├── EnvironmentResolverTests.cs
│   │   │   └── ConfigurationValidatorTests.cs
│   │   ├── Introspection/
│   │   │   ├── SchemaIntrospectorTests.cs    (SQLite in-memory: full introspection pipeline)
│   │   │   ├── TypeMapperTests.cs
│   │   │   └── RelationshipMergerTests.cs
│   │   ├── Models/
│   │   │   ├── OverrideResolverTests.cs
│   │   │   └── VariantResolutionTests.cs
│   │   ├── Rendering/
│   │   │   ├── DispatchTagTests.cs
│   │   │   ├── MacroTagTests.cs
│   │   │   ├── CustomFiltersTests.cs
│   │   │   ├── TemplateRenderingTests.cs
│   │   │   └── FileWriterTests.cs
│   │   ├── Ddl/                              ← new (see section 16)
│   │   │   ├── SqlServerDdlGeneratorTests.cs
│   │   │   ├── PostgreSqlDdlGeneratorTests.cs
│   │   │   ├── MySqlDdlGeneratorTests.cs
│   │   │   ├── SqliteDdlGeneratorTests.cs
│   │   │   └── DdlTypeMapperTests.cs
│   │   ├── Diagrams/                         ← new (see section 16)
│   │   │   └── DiagramGeneratorTests.cs
│   │   ├── Caching/
│   │   │   └── TimestampCacheTests.cs
│   │   ├── Logging/
│   │   │   └── LoggingServiceTests.cs
│   │   ├── Pipeline/
│   │   │   └── GeneratePipelineTests.cs      (end-to-end: SQLite → introspect → override → render → verify output)
│   │   └── Fixtures/
│   │       ├── SqliteTestDatabase.cs         (helper: creates SQLite in-memory DB with tables, FKs, views, indexes)
│   │       ├── SampleConfigs/               (test persistence.project.json files)
│   │       └── SampleTemplates/             (test .liquid template files)
│   │
│   └── Pondhawk.Persistence.Mcp.Tests/       ← MCP integration tests (xUnit)
│       └── Tools/
│           ├── InitToolTests.cs
│           ├── IntrospectSchemaToolTests.cs
│           ├── GenerateToolTests.cs
│           ├── GenerateDdlToolTests.cs       ← new (see section 16)
│           ├── GenerateDiagramToolTests.cs   ← new (see section 16)
│           ├── ListTemplatesToolTests.cs
│           ├── ValidateConfigToolTests.cs
│           └── UpdateToolTests.cs
│
└── build/                                   ← Cake Frosting build project
    ├── Build.csproj                         (Cake.Frosting NuGet reference)
    ├── Program.cs                           (entry point, host setup)
    └── Tasks/
        ├── CleanTask.cs                     (clean bin/obj/publish dirs)
        ├── RestoreTask.cs                   (dotnet restore)
        ├── BuildTask.cs                     (dotnet build)
        ├── TestTask.cs                      (dotnet test for both test projects)
        └── PublishTask.cs                   (dotnet publish for all 4 RIDs)
```

| Concern | `Pondhawk.Persistence.Core` | `Pondhawk.Persistence.Mcp` |
|---------|---------------------------|--------------------------|
| Config parsing + validation | Yes | |
| .env + env var resolution | Yes | |
| Schema introspection | Yes | |
| Type mapping + override resolution | Yes | |
| Template compilation + rendering | Yes | |
| Dispatch tag + macros | Yes | |
| DDL generation (per-dialect SQL) | Yes | |
| HTML ER diagram generation | Yes | |
| File writing | Yes | |
| Caching with timestamp invalidation | Yes | |
| Logging (Serilog setup + MEL integration) | Yes | |
| MCP tool registration | | Yes |
| Parameter parsing + response formatting | | Yes |
| stdio transport + server lifecycle | | Yes |
| `--project` arg handling | | Yes |

Each MCP tool is a thin wrapper: parse MCP parameters → call core library method → format MCP response. A future CLI would follow the same pattern: parse CLI arguments → call core library method → format console output.

### Server Lifecycle and Caching

The MCP server is a long-lived process. The MCP client starts it once and maintains a persistent stdio connection for the duration of the session. All tool calls are handled within that single process.

To avoid redundant I/O, the server caches:

- **Project configuration** — the parsed `persistence.project.json`
- **Compiled templates** — the parsed Liquid template files
- **Schema** — the parsed `db-design.json` file

Before processing each tool call, the server checks whether cached data is still valid:

| Cached Item              | Invalidation Check                                                        |
| ------------------------ | ------------------------------------------------------------------------- |
| Project configuration    | Compare the file's last-modified timestamp against the cached timestamp   |
| Compiled templates       | Compare each `.liquid` file's last-modified timestamp against its cached timestamp |
| Schema                   | Compare `db-design.json`'s last-modified timestamp against the cached timestamp |

If any file has been modified since the cached version was loaded, the server reloads it before proceeding. Cache invalidation cascades:

- If `persistence.project.json` changes → project config and all template caches are invalidated (template paths or filters may have changed)
- If a `.liquid` file changes → only that template's compiled cache is invalidated
- If `db-design.json` changes → only the schema cache is invalidated (this file may be updated by `introspect_schema` or edited manually)

**Special case — TypeMappings write-back:** When `introspect_schema` writes newly discovered types back to `persistence.project.json`, this changes the file's timestamp. To avoid triggering a cascading re-invalidation, the server updates the cached config timestamp immediately after the write-back. The next tool call will see the updated config and matching timestamp.

**Special case — Schema file write-back:** When `introspect_schema` writes `db-design.json`, the server updates the in-memory schema cache and its timestamp simultaneously, so the newly written file is not re-read unnecessarily on the next tool call.

**Concurrency:** MCP tool calls are handled sequentially via the stdio transport (one request at a time). No concurrent access to caches or files occurs.

---

## 4. Partial Class Strategy

pondhawk-mcp uses C# partial classes to cleanly separate generated code from developer-authored customizations. Each entity produces two files:

| File                            | Purpose                        | Overwrite Behavior         |
| ------------------------------- | ------------------------------ | -------------------------- |
| `{EntityName}.generated.cs`     | Generated code from schema     | **Always overwritten**     |
| `{EntityName}.cs`               | Developer stub for custom code | **Only created if missing** |

### How It Works

1. The **generated file** (`Product.generated.cs`) contains the partial class with all properties, attributes, and configuration derived from the database schema. This file is always regenerated and overwritten — developers should never edit it.

2. The **stub file** (`Product.cs`) is a minimal partial class created only once when it doesn't already exist. Developers extend the entity here with custom logic, computed properties, validation, methods, etc. This file is never overwritten.

### Example Output

**`Entities/Product.generated.cs`** — always overwritten:
```csharp
// <auto-generated>
// This file was generated by pondhawk-mcp. Do not edit manually.
// Any changes will be overwritten on next generation.
// </auto-generated>

namespace MyApp.Data.Entities;

public partial class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    public int CategoryId { get; set; }

    // Reference navigation (many-to-one)
    public virtual Category Category { get; set; } = null!;

    // Collection navigation (one-to-many)
    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
```

**`Entities/Product.cs`** — created only if it doesn't exist:
```csharp
namespace MyApp.Data.Entities;

public partial class Product
{
}
```

This pattern allows developers to freely add custom properties, methods, and interface implementations in the stub file without risk of losing them on regeneration.

---

## 5. Configuration

### Design Principle

`persistence.project.json` is **self-contained** — the MCP server reads everything it needs from this single file. There is no separate server-side configuration. The database connection, template paths, output directory, data types, overrides, filtering rules, and logging configuration are all defined in one place, committed to version control alongside the project.

### MCP Server Registration

The MCP server is scoped to a single project. The project path is provided as a command-line argument at startup. The server reads `persistence.project.json` from that path on first use and caches it for the session.

```json
{
  "mcpServers": {
    "pondhawk": {
      "command": "pondhawk-persistence-mcp",
      "args": ["--project", "C:/projects/my-app"]
    }
  }
}
```

Because the project path is known at startup, individual tool calls do not need to specify it. Each MCP server instance is scoped to a single project with a single database connection. For solutions with multiple persistence projects or databases, register a separate MCP server instance per project.

### Project Configuration

Each target project contains a `persistence.project.json` file that is the single source of truth for all code generation settings. This file lives in the project root and is committed to version control.

```json
{
  "Connection": {
    "Provider": "sqlserver",
    "ConnectionString": "${DB_CONNECTION}"
  },
  "OutputDir": "src/Data",
  "DataTypes": {
    "Uid": {
      "ClrType": "string",
      "MaxLength": 28,
      "DefaultValue": "Ulid.NewUlid()"
    },
    "Money": {
      "ClrType": "decimal",
      "DefaultValue": "0m"
    }
  },
  "TypeMappings": [
    { "DbType": "char(28)", "DataType": "Uid" },
    { "DbType": "money", "DataType": "Money" }
  ],
  "Templates": {
    "entity": {
      "Path": "templates/entity.generated.liquid",
      "OutputPattern": "Entities/{{entity.Name | pascal_case}}.generated.cs",
      "Scope": "PerModel",
      "Mode": "Always"
    },
    "entity-stub": {
      "Path": "templates/entity.stub.liquid",
      "OutputPattern": "Entities/{{entity.Name | pascal_case}}.cs",
      "Scope": "PerModel",
      "Mode": "SkipExisting"
    },
    "dbcontext": {
      "Path": "templates/dbcontext.generated.liquid",
      "OutputPattern": "{{config.Defaults.ContextName}}DbContext.generated.cs",
      "Scope": "SingleFile",
      "Mode": "Always"
    },
    "dto": {
      "Path": "templates/dto.generated.liquid",
      "OutputPattern": "Dtos/{{entity.Name | pascal_case}}Dto.generated.cs",
      "Scope": "PerModel",
      "Mode": "Always"
    }
  },
  "Defaults": {
    "Namespace": "MyApp.Data",
    "ContextName": "Inventory",
    "Schema": "dbo",
    "IncludeViews": false,
    "Include": ["Products", "Categories", "Orders*"],
    "Exclude": ["__EFMigrationsHistory", "sysdiagrams"]
  },
  "Relationships": [
    {
      "DependentTable": "Products",
      "DependentColumns": ["CategoryId"],
      "PrincipalTable": "Categories",
      "PrincipalColumns": ["Id"],
      "OnDelete": "NoAction"
    },
    {
      "DependentTable": "OrderItems",
      "DependentColumns": ["OrderId"],
      "PrincipalTable": "Orders",
      "PrincipalColumns": ["Id"]
    }
  ],
  "Logging": {
    "Enabled": false,
    "LogPath": ".pondhawk/logs/pondhawk.log",
    "Level": "Debug",
    "RollingInterval": "Day",
    "RetainedFileCountLimit": 7
  },
  "Overrides": [
    {
      "Class": "*",
      "Property": "Id",
      "DataType": "Uid"
    },
    {
      "Class": "*",
      "Property": "CreatedAt",
      "Artifact": "entity",
      "Variant": "AuditTimestamp"
    },
    {
      "Class": "*",
      "Property": "UpdatedAt",
      "Artifact": "entity",
      "Variant": "AuditTimestamp"
    },
    {
      "Class": "Products",
      "Property": "Price",
      "Artifact": "entity",
      "Variant": "Currency"
    },
    {
      "Class": "Products",
      "Property": "Price",
      "Artifact": "dto",
      "Variant": "FormattedCurrency"
    },
    {
      "Class": "Orders",
      "Artifact": "entity",
      "Variant": "SoftDelete"
    },
    {
      "Class": "*",
      "Property": "RowVersion",
      "Ignore": true
    },
    {
      "Class": "Orders",
      "Property": "InternalNotes",
      "Artifact": "dto",
      "Ignore": true
    }
  ]
}
```

#### Top-Level Configuration Sections

| Section          | Type       | Required | Default | Description                                                            |
| ---------------- | ---------- | -------- | ------- | ---------------------------------------------------------------------- |
| `ProjectName`    | `string`   | No       | —       | Project name used in output file names and diagram title bar           |
| `Description`    | `string`   | No       | —       | Optional project description shown in DDL header and diagram title bar |
| `Connection`     | `object`   | Yes      | —       | Database connection (provider and connection string)                   |
| `OutputDir`      | `string`   | Yes      | —       | Base directory for generated files, relative to project root or absolute. Directories are created automatically if they don't exist. |
| `Templates`      | `object`   | Yes      | —       | Named template definitions (at least one required for generation)      |
| `Defaults`       | `object`   | No       | `{}`    | Default values for namespace, schema, filtering, and view inclusion    |
| `DataTypes`      | `object`   | No       | `{}`    | Custom semantic data types with CLR type, constraints, and defaults    |
| `TypeMappings`   | `array`    | No       | `[]`    | Maps database column types to `DataTypes` entries or direct CLR types. Auto-populated by `introspect_schema`. |
| `Relationships`  | `array`    | No       | `[]`    | Explicit table relationships that supplement or override introspected FKs |
| `Overrides`      | `array`    | No       | `[]`    | Per-class/property variant, data type, and ignore rules (see section 9) |
| `Logging`        | `object`   | No       | Disabled | Diagnostic file logging configuration (see Logging subsection below)  |

#### Connection Fields

The `Connection` object specifies the database provider and connection string:

| Field              | Type     | Required | Description                                                              |
| ------------------ | -------- | -------- | ------------------------------------------------------------------------ |
| `Provider`         | `string` | Yes      | Database provider identifier. Valid values: `sqlserver`, `postgresql`, `mysql`, `mariadb`, `sqlite` |
| `ConnectionString` | `string` | Yes      | ADO.NET connection string. Supports `${VAR}` substitution (see below). |

Provider-to-ADO.NET mapping: `sqlserver` → Microsoft.Data.SqlClient, `postgresql` → Npgsql, `mysql` → MySqlConnector, `mariadb` → MySqlConnector, `sqlite` → Microsoft.Data.Sqlite. The `mysql` and `mariadb` providers both use MySqlConnector but may produce different default type mappings.

#### OutputDir

`OutputDir` (string, required) — the base output directory for all generated files. `OutputPattern` values in templates are resolved relative to this directory. If the directory does not exist, it is created automatically. The path is relative to the project root (the directory containing `persistence.project.json`) or absolute.

#### Template Entry Fields

Each entry in `Templates` is a named key (the artifact name, e.g., `"entity"`) mapping to an object with:

| Field           | Type     | Required | Description                                                           |
| --------------- | -------- | -------- | --------------------------------------------------------------------- |
| `Path`          | `string` | Yes      | Path to the `.liquid` template file, relative to the project root     |
| `OutputPattern` | `string` | Yes      | Liquid expression for the output file path, resolved relative to `OutputDir`. The same template context variables are available: `entity`, `schema`, `config`, `database`, `parameters` (PerModel) or `entities`, `views`, `schemas`, `config`, `database`, `parameters` (SingleFile). |
| `Scope`         | `string` | Yes      | `PerModel` (one file per table/view) or `SingleFile` (one file total) |
| `Mode`          | `string` | Yes      | `Always` (overwrite) or `SkipExisting` (create only if file absent)   |
| `AppliesTo`     | `string` | No       | Limits which model kinds the template runs for: `Tables`, `Views`, or `All` (default). When set to `Tables`, the template only runs for table models; when `Views`, only for view models. |

#### Empty Output Skipping

Templates that render to whitespace-only output are automatically skipped — no file is written to disk. This is useful when a template uses guard conditions (e.g., `{% if entity.IsView == false %}...{% endif %}`) that may produce empty output for certain models. Skipped files are reported as `SkippedEmpty` in the `generate` response.

#### Environment Variable Substitution

Connection strings support environment variable substitution using `${VARIABLE_NAME}` syntax. The server resolves these at runtime before use. The `.env` file exists solely to keep database credentials out of version control — all other configuration belongs directly in `persistence.project.json`.

```json
"ConnectionString": "${INVENTORY_DB_CONNECTION}"
```

This allows `persistence.project.json` to be safely committed to version control without exposing secrets.

At startup, the server loads a `.env` file from the same directory as `persistence.project.json` (if present). Variables defined in `.env` are available for substitution alongside system environment variables. The `.env` file should be added to `.gitignore` and never committed.

**`.env` file format:**

```
# Comments start with #
INVENTORY_DB_CONNECTION=Server=localhost;Database=Inventory;Trusted_Connection=true;
ORDERS_DB_CONNECTION="Host=localhost;Database=Orders;Username=dev;Password=secret;"
```

- One `KEY=VALUE` pair per line
- Lines starting with `#` are comments (ignored)
- Empty lines are ignored
- Values may optionally be wrapped in double quotes (`"value"`) — quotes are stripped
- No multi-line values, no `export` prefix, no variable interpolation within `.env`

- `${VAR}` is replaced with the value of the variable
- The `.env` file is loaded first; system environment variables override `.env` values if both are set
- If the referenced variable is not set in either source, the server returns a clear error at the point of use (e.g., when `introspect_schema` or `generate` is called)
- Substitution is **only** supported in `ConnectionString` values — not in other configuration fields

#### Defaults Fields

| Field          | Type       | Required | Default | Description                                                              |
| -------------- | ---------- | -------- | ------- | ------------------------------------------------------------------------ |
| `Namespace`    | `string`   | No       | —       | Default namespace for generated code. Templates reference via `config.Defaults.Namespace`. |
| `ContextName`  | `string`   | No       | —       | Name prefix for the DbContext class. Templates reference via `config.Defaults.ContextName`. |
| `Schema`       | `string`   | No       | `"dbo"` | Default schema name, used as the fallback for `DependentSchema` / `PrincipalSchema` in `Relationships`. Does **not** filter introspection — the `schemas` tool parameter controls that (default: all schemas). |
| `IncludeViews` | `bool`     | No       | `false` | Whether to include views in introspection and generation |
| `Include`      | `string[]` | No       | all     | Table/view name patterns to include (supports wildcards) |
| `Exclude`      | `string[]` | No       | none    | Table/view name patterns to exclude (supports wildcards). System tables like `__EFMigrationsHistory` are always excluded regardless. |

`Namespace` and `ContextName` have no built-in defaults. If a template references a missing Defaults field (e.g., `config.Defaults.ContextName` when `ContextName` is not set), Fluid's strict mode will throw a `FluidException`. The AI agent should ensure these fields are set when templates use them.

#### Logging

The `Logging` section controls diagnostic file logging. When enabled, the server writes structured logs to a rolling file. This is intended for bug resolution and should typically be left disabled during normal use.

| Field                    | Type     | Required | Default                       | Description                                                                                          |
| ------------------------ | -------- | -------- | ----------------------------- | ---------------------------------------------------------------------------------------------------- |
| `Enabled`                | `bool`   | No       | `false`                       | Master switch. When `false`, no log file is created and logging has zero overhead.                    |
| `LogPath`                | `string` | No       | `.pondhawk/logs/pondhawk.log`   | Path to the log file, relative to the project directory or absolute.                                 |
| `Level`                  | `string` | No       | `Debug`                       | Minimum log level. One of: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`.           |
| `RollingInterval`        | `string` | No       | `Day`                         | How often to roll to a new file. One of: `Infinite`, `Year`, `Month`, `Day`, `Hour`, `Minute`.       |
| `RetainedFileCountLimit` | `int`    | No       | `7`                           | Maximum number of rolled log files to keep. Older files are deleted automatically. `0` = unlimited.   |

**MEL interception:** The server registers Serilog as the provider for `Microsoft.Extensions.Logging` via `Serilog.Extensions.Logging`. This means all log output from .NET runtime internals, third-party NuGet packages (DatabaseSchemaReader, Fluid, ModelContextProtocol SDK), and the server's own code flows through the same Serilog pipeline and into the same log file. When logging is disabled, a silent (no-op) logger is configured so MEL calls from third-party code have negligible overhead.

**What is logged** (when enabled at `Debug` level):

| Category | Examples |
|----------|----------|
| Startup | Project path, config loaded, provider versions, logging config itself |
| Tool calls | Tool name, parameters (with connection strings redacted), duration, success/failure |
| Schema introspection | Provider, tables/views discovered, columns per table, type mappings applied, `db-design.json` write |
| Template rendering | Template path, artifact name, scope, models rendered, explicit relationships merged, dispatch macro calls, rendering duration per file |
| Override resolution | Override rule matched, specificity decision, variant resolved, properties ignored |
| Cache | Cache hits/misses, invalidation events, timestamps compared |
| Errors | Full exception details including stack traces (credentials are never logged) |

**Security:** Connection strings and credentials are never written to log files. The server redacts `ConnectionString` values before logging.

#### Template Scope

| Scope        | Behavior                                                                    |
| ------------ | --------------------------------------------------------------------------- |
| `PerModel`  | Template is rendered once per matched table/view. Context includes `entity` and `schema` (the entity's parent schema). |
| `SingleFile`  | Template is rendered once for the entire matched result set. Context includes `entities` and `views` (all matched tables/views across all schemas) and `schemas` (all matched schema objects). |

#### Template Modes

| Mode            | Behavior                                                  |
| --------------- | --------------------------------------------------------- |
| `Always`        | Always write the file, overwriting any existing content    |
| `SkipExisting`  | Only write the file if it does not already exist on disk   |

#### Multi-Schema Output Paths

When a database has multiple schemas, `OutputPattern` should include `entity.Schema` to avoid file collisions. For example, if both `dbo.Products` and `sales.Products` exist:

```
"OutputPattern": "Entities/{{entity.Schema | pascal_case}}/{{entity.Name | pascal_case}}.generated.cs"
```

This produces `Entities/Dbo/Products.generated.cs` and `Entities/Sales/Products.generated.cs`. For single-schema databases, `entity.Schema` can be omitted from the path. The `validate_config` tool will warn if multiple entities would resolve to the same output path.

#### Relationships

The `Relationships` section defines explicit table relationships that supplement or override foreign key constraints introspected from the database. This is essential for databases that do not use FK constraints, and for adding relationships that exist logically but are not expressed in the schema.

```json
{
  "Relationships": [
    {
      "DependentTable": "Products",
      "DependentSchema": "sales",
      "DependentColumns": ["CategoryId"],
      "PrincipalTable": "Categories",
      "PrincipalSchema": "dbo",
      "PrincipalColumns": ["Id"],
      "OnDelete": "NoAction"
    }
  ]
}
```

| Field              | Type       | Required | Description                                                  |
| ------------------ | ---------- | -------- | ------------------------------------------------------------ |
| `DependentTable`   | `string`   | Yes      | Table that holds the foreign key column(s)                   |
| `DependentSchema`  | `string`   | No       | Schema of the dependent table. Defaults to `Defaults.Schema` |
| `DependentColumns` | `string[]` | Yes      | Column(s) on the dependent table                             |
| `PrincipalTable`   | `string`   | Yes      | Table being referenced                                       |
| `PrincipalSchema`  | `string`   | No       | Schema of the principal table. Defaults to `Defaults.Schema` |
| `PrincipalColumns` | `string[]` | Yes      | Column(s) on the principal table (typically the primary key)  |
| `OnDelete`         | `string`   | No       | Delete behavior (`Cascade`, `SetNull`, `NoAction`). Default: `NoAction` |

**Resolution order:**

1. **Explicit `Relationships`** in `persistence.project.json` — highest priority; these always apply
2. **Introspected FK constraints** — detected from the database schema

If an explicit relationship matches an introspected FK (same dependent table, schema, and columns), the explicit definition wins. This allows overriding properties like `OnDelete` on an existing FK.

Explicit relationships are **not** written to `db-design.json` — that file only contains what is actually in the database. Instead, the `generate` tool merges explicit relationships with the introspected schema data at generation time, producing a unified in-memory model for template rendering. Templates see both sources identically through `ForeignKeys[]` and `ReferencingForeignKeys[]` — they have no way to distinguish between the two sources. This means changes to `Relationships` in `persistence.project.json` take effect on the next `generate` call without needing to re-run `introspect_schema`.

### Project Structure (Template Assets)

```
my-dotnet-project/
├── persistence.project.json      ← project configuration (version controlled)
├── persistence.project.schema.json ← JSON Schema for IDE autocompletion of project config (version controlled, written by init/update)
├── db-design.json                   ← introspected or design-first database schema (version controlled, written by introspect_schema or authored by AI)
├── db-design.schema.json            ← JSON Schema for IDE autocompletion of db-design.json (version controlled, written by init/update)
├── AGENTS.md                     ← generated by init/update, teaches AI agents how to use the system
├── conventions.md                ← optional, describes naming conventions for AI inference
├── .env                          ← generated by init, .gitignored, contains database credentials
├── templates/
│   ├── entity.generated.liquid
│   ├── entity.stub.liquid
│   ├── dbcontext.generated.liquid
│   └── dto.generated.liquid
├── src/
│   └── Data/
│       └── Entities/
│           ├── Product.generated.cs    ← always overwritten
│           ├── Product.cs              ← created once, never overwritten
│           ├── Category.generated.cs
│           └── Category.cs
```

---

## 6. MCP Tools

### 6.1 `init`

Creates a skeleton `persistence.project.json`, starter Liquid templates, a `.env` file for database credentials, and an `AGENTS.md` instructions file in the project directory. This bootstraps a new project with a working configuration and teaches the AI agent how to use the system.

**Parameters:**

| Parameter    | Type     | Required | Description                                                      |
| ------------ | -------- | -------- | ---------------------------------------------------------------- |
| `provider`         | `string` | No       | Database provider (`sqlserver`, `postgresql`, `mysql`, `mariadb`, `sqlite`). Default: `sqlserver` |
| `namespace`        | `string` | No       | Default namespace for generated code. Default: `MyApp.Data`      |
| `connectionString` | `string` | No       | Database connection string. If provided, written to `.env` so `introspect_schema` works immediately. If omitted, `.env` is created with a provider-specific placeholder. |

**Behavior:**

- If `persistence.project.json` already exists, returns an error (never overwrites an existing config)
- Creates `persistence.project.json` with the configured connection, default settings, logging section (disabled by default), and two template entries (entity + entity-stub)
- Creates `persistence.project.schema.json` — JSON Schema for IDE autocompletion and validation of `persistence.project.json`
- Creates `db-design.schema.json` — JSON Schema for IDE autocompletion and validation of `db-design.json` (see section 16.2.1)
- Creates `templates/entity.generated.liquid` — a working PerModel template with `DefaultClass`, `DefaultProperty` macros, dispatch tags, and FK/collection navigation loops
- Creates `templates/entity.stub.liquid` — a minimal SkipExisting stub template
- Creates `AGENTS.md` — comprehensive instructions for AI agents (see section 9, subsection "AI Agent Instructions (`AGENTS.md`)")
- Creates `.env` — if `connectionString` is provided, writes the real value as `DB_CONNECTION=<value>` so `introspect_schema` works immediately; otherwise writes a provider-specific placeholder (e.g., SQL Server uses `Trusted_Connection`, PostgreSQL uses `Host`/`Username`/`Password`). Skipped if `.env` already exists.

**Generated skeleton `persistence.project.json`** includes these sections:

- `Connection` — the specified `provider` and a placeholder `"${DB_CONNECTION}"` connection string
- `OutputDir` — `"src/Data"`
- `Templates` — `"entity"` (PerModel, Always) and `"entity-stub"` (PerModel, SkipExisting)
- `Defaults` — `Namespace` from parameter (default `"MyApp.Data"`), `ContextName` as `"MyApp"`, `Schema` as `"dbo"`, `IncludeViews` as `false`
- `Logging` — `Enabled: false` with all other fields at defaults
- Empty `DataTypes`, `TypeMappings`, `Relationships`, and `Overrides` sections

**Returns:**

```json
{
  "FilesCreated": [
    "persistence.project.json",
    "persistence.project.schema.json",
    "db-design.schema.json",
    "AGENTS.md",
    ".env",
    "templates/entity.generated.liquid",
    "templates/entity.stub.liquid"
  ],
  "NextSteps": "Read AGENTS.md for full usage instructions. Update the connection string in .env and run introspect_schema to verify connectivity."
}
```

---

### 6.2 `introspect_schema`

Introspects the database schema using the project's configured connection and writes the introspected schema metadata to `db-design.json` in the project directory (alongside `persistence.project.json`). Returns a lightweight summary to avoid flooding the AI agent's context window with large schema payloads.

The `db-design.json` file contains **only what is actually in the database** — introspected tables, views, columns, primary keys, foreign keys, and indexes. Explicit `Relationships` from `persistence.project.json` are **not** included in `db-design.json`; they are merged at generation time by the `generate` tool. This keeps `db-design.json` as a pure snapshot of the database schema.

`db-design.json` is a version-controlled project artifact — once introspected, any team member can generate code without a live database connection.

As a side effect, introspection automatically populates the `TypeMappings` section in `persistence.project.json` with any newly discovered database types and their default CLR type mappings. This is add-only — existing entries are never modified or removed. See "Automatic Type Mapping Population" below.

**Parameters:**

| Parameter    | Type       | Required | Description                                                 |
| ------------ | ---------- | -------- | ----------------------------------------------------------- |
| `schemas`      | `string[]` | No       | Schema names to include (default: all)                      |
| `include`      | `string[]` | No       | Table/view name patterns to include (supports wildcards). When omitted, falls back to `Defaults.Include`. |
| `exclude`      | `string[]` | No       | Table/view name patterns to exclude (supports wildcards). When omitted, falls back to `Defaults.Exclude`. |
| `includeViews` | `bool`     | No       | Whether to include views in the result (default: value from `Defaults.IncludeViews`, which defaults to `false`) |

**Behavior:**

- If `db-design.json` already exists and has `"Origin": "design"`, returns an error refusing to overwrite (see "Origin-based overwrite protection" in section 7)
- Connects to the database using the project's `Connection` settings
- Introspects tables, views, columns, primary keys, foreign keys, and indexes
- Writes the introspected schema metadata to `db-design.json` in the project directory with `"Origin": "introspected"` (explicit `Relationships` from config are **not** included — they are merged at generation time), including a `$schema` reference to `db-design.schema.json` for IDE validation
- Auto-populates `TypeMappings` in `persistence.project.json` with newly discovered types
- Returns a summary with table/view names and column counts (not the full schema)

**`db-design.json` format** (written to disk — the introspected database schema only, no explicit relationships from config):

```json
{
  "$schema": "db-design.schema.json",
  "Origin": "introspected",
  "Database": "Inventory",
  "Provider": "sqlserver",
  "Schemas": [
    {
      "Name": "dbo",
      "Tables": [
        {
          "Name": "Products",
          "Schema": "dbo",
          "Columns": [
            {
              "Name": "Id",
              "DataType": "int",
              "ClrType": "int",
              "IsNullable": false,
              "IsPrimaryKey": true,
              "IsIdentity": true,
              "MaxLength": null,
              "Precision": 10,
              "Scale": 0,
              "DefaultValue": null
            },
            {
              "Name": "Name",
              "DataType": "nvarchar",
              "ClrType": "string",
              "IsNullable": false,
              "IsPrimaryKey": false,
              "IsIdentity": false,
              "MaxLength": 200,
              "Precision": null,
              "Scale": null,
              "DefaultValue": null
            }
          ],
          "PrimaryKey": {
            "Name": "PK_Products",
            "Columns": ["Id"]
          },
          "ForeignKeys": [
            {
              "Name": "FK_Products_Categories",
              "Columns": ["CategoryId"],
              "PrincipalTable": "Categories",
              "PrincipalSchema": "dbo",
              "PrincipalColumns": ["Id"],
              "OnDelete": "Cascade"
            }
          ],
          "Indexes": [
            {
              "Name": "IX_Products_CategoryId",
              "Columns": ["CategoryId"],
              "IsUnique": false
            }
          ],
          "ReferencingForeignKeys": []
        }
      ],
      "Views": []
    }
  ]
}
```

**Returns** (MCP response — lightweight summary):

```json
{
  "Database": "Inventory",
  "Provider": "sqlserver",
  "SchemaFile": "db-design.json",
  "Summary": {
    "Schemas": ["dbo"],
    "Tables": [
      { "Name": "Products", "Schema": "dbo", "Columns": 8, "ForeignKeys": 1 },
      { "Name": "Categories", "Schema": "dbo", "Columns": 3, "ForeignKeys": 0 }
    ],
    "Views": [],
    "TypeMappingsAdded": 3
  }
}
```

#### Automatic Type Mapping Population

Each time `introspect_schema` runs, the server scans all discovered column types and compares them against the existing `TypeMappings` entries in `persistence.project.json`. Any database type not already present is added with its default CLR type mapping for the provider.

**Behavior:**

- **Add-only** — new entries are appended for discovered types that have no existing `TypeMappings` entry. Existing entries are never modified or removed.
- **Idempotent** — running introspection multiple times only adds entries for newly discovered types. Previously populated entries are untouched.
- **Preserves customizations** — if the AI or developer has changed an entry (e.g., mapped `char(28)` to `DataType: "Uid"`), that customization is preserved.
- **Uses built-in defaults** — new entries use the provider's built-in default CLR type mapping.

**Example:** After the first introspection of a SQL Server database, `TypeMappings` is auto-populated:

```json
"TypeMappings": [
  { "DbType": "int", "ClrType": "int" },
  { "DbType": "nvarchar", "ClrType": "string" },
  { "DbType": "datetime2", "ClrType": "DateTime" },
  { "DbType": "decimal", "ClrType": "decimal" },
  { "DbType": "bit", "ClrType": "bool" },
  { "DbType": "char(28)", "ClrType": "string" }
]
```

The AI can then customize specific entries:

```json
  { "DbType": "char(28)", "DataType": "Uid" }
```

On subsequent introspections, if a new type is encountered (e.g., `varbinary`), only that type is added. The `char(28) → Uid` customization remains unchanged.

---

### 6.3 `generate`

Generates code by rendering Liquid templates against schema data from `db-design.json`, merged with explicit `Relationships` from `persistence.project.json`. Reads the project configuration, schema file, and templates from the project directory, then writes the generated files to disk. **No database connection is required** — generation is purely file-driven.

**Schema data:** The `generate` tool reads schema data from `db-design.json` in the project directory and merges explicit `Relationships` from `persistence.project.json` into the in-memory models before rendering. This means changes to `Relationships` take effect immediately on the next `generate` call without re-running `introspect_schema`. If `db-design.json` does not exist, the tool returns an error instructing the AI agent to run `introspect_schema` first.

**Filtering:** When `models` is provided, it completely overrides the config-level `Defaults.Include` and `Defaults.Exclude` patterns — only the exact names listed in `models` are generated. When `models` is omitted, the config-level `Include`/`Exclude` defaults apply.

**Parameters:**

| Parameter    | Type       | Required | Description                                                            |
| ------------ | ---------- | -------- | ---------------------------------------------------------------------- |
| `templates`  | `string[]` | No       | Template keys from `persistence.project.json` to run (default: all)    |
| `models`       | `string[]` | No       | Exact table/view names to generate for, no wildcards. When provided, completely overrides `Defaults.Include`/`Exclude`. |
| `schemas`      | `string[]` | No       | Schema filter (overrides config defaults)                              |
| `includeViews` | `bool`     | No       | Whether to include views (default: value from `Defaults.IncludeViews`) |
| `parameters`   | `object`   | No       | Additional key-value pairs passed to the template context. Values should be simple types (strings, numbers, booleans) usable in Liquid expressions. |

**Returns:**

```json
{
  "OutputDir": "C:/projects/my-app/src/Data",
  "FilesWritten": [
    { "Path": "Entities/Product.generated.cs", "Action": "Overwritten" },
    { "Path": "Entities/Product.cs", "Action": "Created" },
    { "Path": "Entities/Category.generated.cs", "Action": "Overwritten" },
    { "Path": "Entities/Category.cs", "Action": "SkippedExisting" },
    { "Path": "InventoryDbContext.generated.cs", "Action": "Overwritten" }
  ],
  "Summary": "3 files written, 1 file created, 1 file skipped"
}
```

---

### 6.4 `list_templates`

Lists available templates from the project's `persistence.project.json` configuration.

**Parameters:** None

**Returns:**

```json
{
  "Templates": [
    {
      "Key": "entity",
      "Path": "templates/entity.generated.liquid",
      "OutputPattern": "Entities/{{entity.Name | pascal_case}}.generated.cs",
      "Scope": "PerModel",
      "Mode": "Always"
    },
    {
      "Key": "entity-stub",
      "Path": "templates/entity.stub.liquid",
      "OutputPattern": "Entities/{{entity.Name | pascal_case}}.cs",
      "Scope": "PerModel",
      "Mode": "SkipExisting"
    }
  ]
}
```

---

### 6.5 `validate_config`

Validates the project's `persistence.project.json` and its referenced templates without connecting to any database.

**Parameters:** None

**Validation checks performed:**

| Category | Check | Severity |
|----------|-------|----------|
| Required sections | `Connection`, `OutputDir`, `Templates` are present | Error |
| Required fields | `Connection` has `Provider` and `ConnectionString`; each template has `Path`, `OutputPattern`, `Scope`, `Mode` | Error |
| Valid enum values | `Provider` is one of `sqlserver`/`postgresql`/`mysql`/`mariadb`/`sqlite`; `Scope` is `PerModel`/`SingleFile`; `Mode` is `Always`/`SkipExisting`; `Logging.Level` and `Logging.RollingInterval` are valid values | Error |
| Template files exist | Each template's `Path` resolves to a file on disk | Error |
| Template parsing | Each template file is valid Liquid (parsed with `TryParse`) | Error |
| DataType references | `DataType` values in `TypeMappings` and `Overrides` reference entries defined in `DataTypes` | Error |
| Relationship references | `DependentTable` and `PrincipalTable` in `Relationships` are non-empty strings | Error |
| Output path collisions | Multiple templates/entities would produce the same output file path | Warning |
| Unresolved env vars | `${VAR}` references in connection strings where the variable is not set | Warning |

**Returns:**

```json
{
  "Valid": true,
  "Errors": [],
  "Warnings": ["Output path collision: 'Entities/Products.generated.cs' produced by both entity and dto templates"]
}
```

---

### 6.6 `update`

Refreshes project files after upgrading pondhawk-mcp. When the MCP server is upgraded, existing projects retain stale copies of `AGENTS.md`, `persistence.project.schema.json`, and `db-design.schema.json`. The AI agent won't learn about new features (like design-first DDL generation or diagram generation) and IDE autocompletion may be outdated. The `init` tool refuses to overwrite an existing project, so a separate `update` tool is needed.

**Parameters:** None

**Behavior:**

- If `persistence.project.json` does not exist, returns an error instructing the user to run `init` first
- Overwrites `AGENTS.md` with the latest embedded instructions so the AI agent knows about all current features
- Overwrites `persistence.project.schema.json` with the latest JSON Schema so IDE autocompletion and `validate_config` recognize new properties
- Overwrites `db-design.schema.json` with the latest JSON Schema for `db-design.json` validation (see section 16.2.1)
- Normalizes `persistence.project.json` via a load/save round-trip — this picks up new default values and serialization changes without losing any existing configuration
- Updates the config cache timestamp after the write-back to avoid unnecessary cache invalidation
- All existing configuration values (Templates, TypeMappings, Overrides, Relationships, DataTypes, Logging, etc.) are preserved during normalization

**Returns:**

```json
{
  "FilesUpdated": [
    "AGENTS.md",
    "persistence.project.schema.json",
    "db-design.schema.json",
    "persistence.project.json"
  ],
  "Message": "Project files updated to the latest version. AGENTS.md and JSON Schemas are current; config has been normalized."
}
```

---

## 7. Schema Introspection Model

Schema introspection is powered by [DatabaseSchemaReader](https://github.com/martinjw/dbschemareader), a cross-database library that reads metadata from any ADO.NET provider into a single normalized model. The server maps DatabaseSchemaReader's output into two internal types — **Model** and **Attribute** — that are passed to Liquid templates and flow through the dispatch system.

### Mapping from Database Schema to Internal Types

| Database Concept | Internal Type | Notes |
|-----------------|---------------|-------|
| Table           | Model         | `IsView` = `false` |
| View            | Model         | `IsView` = `true` |
| Column          | Attribute     | Same mapping for both table and view columns |

Tables and views are both mapped to **Model**. They are treated identically by the template engine, the dispatch system, and the override system. Templates can check `entity.IsView` if they need to differentiate.

### Model (mapped from Tables and Views)

The Model type represents a table or view. This is the object available as `entity` in PerModel templates and as elements of `entities` / `views` in SingleFile templates.

```
Model
├── Name: string                    (table or view name)
├── Schema: string                  (schema name, e.g., "dbo")
├── IsView: bool                    (true for views, false for tables)
├── Attributes: Attribute[]         (mapped from Columns)
├── PrimaryKey
│   ├── Name: string
│   └── Columns: string[]
├── Note: string?                      (optional — descriptive note, emitted as SQL comment in DDL; design-first only)
├── ForeignKeys[]                   ← "I reference these models"
│   ├── Name: string?                 (optional — auto-generated as FK_{table}_{ref} if null)
│   ├── Columns: string[]
│   ├── PrincipalTable: string
│   ├── PrincipalSchema: string
│   ├── PrincipalColumns: string[]
│   ├── OnDelete: string
│   └── OnUpdate: string?             (optional — update behavior: Cascade, SetNull, SetDefault, NoAction, Restrict)
├── ReferencingForeignKeys[]        ← "These models reference me"
│   ├── Name: string
│   ├── Table: string               (the dependent model holding the FK)
│   ├── Schema: string
│   ├── Columns: string[]           (FK columns on the dependent model)
│   └── PrincipalColumns: string[]  (my columns being referenced)
├── Indexes[]
│   ├── Name: string
│   ├── Columns: string[]
│   └── IsUnique: bool
└── GetVariant(artifactName): string  (resolves the variant name for the given artifact)
```

### Attribute (mapped from Columns)

The Attribute type represents a column on a table or view. This is the object available when iterating `entity.Attributes` in templates.

```
Attribute
├── Name: string                    (column name)
├── DataType: string                (native DB type, e.g., "nvarchar", "int")
├── ClrType: string?                (mapped .NET type, e.g., "string", "int" — optional for design-first schemas)
├── IsNullable: bool
├── IsPrimaryKey: bool
├── IsIdentity: bool
├── MaxLength: int?
├── Precision: int?
├── Scale: int?
├── DefaultValue: string?
├── Note: string?                   (optional — descriptive note, emitted as SQL comment in DDL; design-first only)
└── GetVariant(artifactName): string  (resolves the variant name for the given artifact)
```

### Dispatch Type Discrimination

The `{% dispatch %}` tag determines the macro suffix by checking the runtime type of the object:

- **Model** → appends `Class` suffix (fallback: `DefaultClass`)
- **Attribute** → appends `Property` suffix (fallback: `DefaultProperty`)

Since views are Models, `{% dispatch entity %}` on a view behaves identically to a table — it resolves the variant via `GetVariant(artifactName)`, appends `Class`, and calls the matching macro.

### Introspection Wire Format

The `introspect_schema` tool returns the raw introspected data before Model/Attribute mapping. The wire format uses database terminology (Tables, Views, Columns) while templates use the mapped types (Model, Attribute):

```
DatabaseSchema
├── Origin: string                        ("introspected" or "design" — controls overwrite protection; see below)
├── Database: string?                     (optional for design-first schemas)
├── Provider: string?                     (optional for design-first schemas — generator takes provider as parameter)
├── Enums[]                               (optional — enum type definitions for design-first schemas)
│   ├── Name: string
│   ├── Note: string?                     (optional descriptive note)
│   └── Values[]
│       ├── Name: string
│       └── Note: string?                 (optional descriptive note)
└── Schemas[]
    ├── Name: string
    ├── Tables[]                          → mapped to Model[]
    │   ├── Name: string
    │   ├── Schema: string                (from parent Schemas[] element)
    │   ├── Note: string?                 (optional — emitted as SQL comment in DDL)
    │   ├── PrimaryKey
    │   │   ├── Name: string?             (optional — auto-generated as PK_{table} if null)
    │   │   └── Columns: string[]
    │   ├── Columns[]                     → mapped to Attribute[]
    │   │   ├── Name, DataType, IsNullable, IsPrimaryKey, IsIdentity
    │   │   ├── MaxLength, Precision, Scale, DefaultValue
    │   │   ├── Note: string?             (optional — emitted as SQL comment in DDL)
    │   │   └── ClrType (resolved via TypeMapping — optional for design-first schemas)
    │   ├── ForeignKeys[]
    │   │   ├── Name: string?             (optional — auto-generated as FK_{table}_{ref} if null)
    │   │   ├── Columns: string[]         (FK columns on this table)
    │   │   ├── PrincipalTable, PrincipalSchema, PrincipalColumns
    │   │   ├── OnDelete: string
    │   │   └── OnUpdate: string?         (optional — Cascade, SetNull, SetDefault, NoAction, Restrict)
    │   ├── ReferencingForeignKeys[]      (inverse: FKs from other tables pointing here — optional, computed from ForeignKeys if absent)
    │   │   ├── Name: string
    │   │   ├── Table: string             (the dependent table holding the FK)
    │   │   ├── Schema: string
    │   │   ├── Columns: string[]         (FK columns on the dependent table)
    │   │   └── PrincipalColumns: string[]
    │   └── Indexes[]
    │       ├── Name: string?             (optional — auto-generated as IX_{table}_{cols} if null)
    │       ├── Columns: string[]
    │       └── IsUnique: bool
    └── Views[]                           → mapped to Model[] (IsView = true)
        ├── Name: string
        ├── Schema: string                (from parent Schemas[] element)
        └── Columns[]                     → mapped to Attribute[]
```

For views, `PrimaryKey` is null and `ForeignKeys`, `ReferencingForeignKeys`, and `Indexes` are empty arrays, since views do not have key or index metadata in most databases.

**Design-first compatibility:** The schema wire format supports both introspected and design-first schemas. For design-first use, Claude writes `db-design.json` directly with `"Origin": "design"` — `ClrType` on columns, `ReferencingForeignKeys[]` on tables, `Provider`, and `Database` are all optional. The `Enums[]` and `Note` fields are only used for design-first schemas and DDL generation; `introspect_schema` does not populate them. See section 16.2 for the full specification of db-design.json extensions.

**Origin-based overwrite protection:** The `Origin` field in `db-design.json` controls whether `introspect_schema` is allowed to overwrite the file:

- `"Origin": "introspected"` — written by `introspect_schema`. The file can be freely overwritten by subsequent introspection calls.
- `"Origin": "design"` — written by the AI agent when hand-authoring a schema. `introspect_schema` **refuses to overwrite** the file and returns an error explaining that the schema is design-first. To switch back to database-first, the user must manually delete `db-design.json` or change `Origin` to `"introspected"`.

This prevents an accidental `introspect_schema` call from silently replacing a carefully crafted design-first schema.

### Type Mapping

Type mapping determines how native database types are translated to .NET CLR types on the Attribute model before template rendering. It operates in two layers:

1. **Built-in defaults** — the server ships with sensible mappings for each database provider
2. **Project-level entries** — `persistence.project.json` `TypeMappings` can override or extend the built-ins

The `TypeMappings` section is automatically populated by `introspect_schema` (see section 6.3). On each introspection, any newly discovered database type is added with its built-in default CLR mapping. This makes all active type mappings explicit and visible in the project file, where the AI or developer can customize them.

#### Built-in Default Mappings

Each database provider includes a default mapping from native types to CLR types:

| SQL Server          | PostgreSQL       | MySQL / MariaDB  | SQLite      | CLR Type   |
| ------------------- | ---------------- | ---------------- | ----------- | ---------- |
| `int`               | `integer`        | `int`            | `INTEGER`   | `int`      |
| `bigint`            | `bigint`         | `bigint`         | `INTEGER`   | `long`     |
| `bit`               | `boolean`        | `tinyint(1)`     | `INTEGER`   | `bool`     |
| `nvarchar`          | `text`/`varchar` | `varchar`        | `TEXT`       | `string`   |
| `datetime2`         | `timestamp`      | `datetime`       | `TEXT`       | `DateTime` |
| `decimal`           | `numeric`        | `decimal`        | `REAL`       | `decimal`  |
| `uniqueidentifier`  | `uuid`           | `char(36)`       | `TEXT`       | `Guid`     |
| `varbinary`         | `bytea`          | `blob`           | `BLOB`       | `byte[]`   |

MariaDB shares the MySQL column in this table — `MySqlConnector` handles both with the same type mappings. SQLite uses a simplified type affinity system; the mappings above cover the most common patterns.

#### Project-Level Type Mappings

The `TypeMappings` section in `persistence.project.json` overrides or extends the built-in defaults. Each entry maps a native database type to either a direct CLR type or a named custom data type defined in `DataTypes`.

```json
{
  "DataTypes": {
    "Uid": {
      "ClrType": "string",
      "MaxLength": 28,
      "DefaultValue": "Ulid.NewUlid()"
    },
    "Money": {
      "ClrType": "decimal",
      "DefaultValue": "0m"
    },
    "ShortText": {
      "ClrType": "string",
      "MaxLength": 100
    }
  },
  "TypeMappings": [
    { "DbType": "char(28)", "DataType": "Uid" },
    { "DbType": "money", "DataType": "Money" },
    { "DbType": "nvarchar(100)", "DataType": "ShortText" },
    { "DbType": "tinyint", "ClrType": "byte" },
    { "DbType": "xml", "ClrType": "string" }
  ]
}
```

Each mapping entry supports two target modes:

| Field      | Type     | Required | Description                                                        |
| ---------- | -------- | -------- | ------------------------------------------------------------------ |
| `DbType`   | `string` | Yes      | Native database type to match (from introspected schema)           |
| `DataType` | `string` | No*      | Name of a custom data type defined in `DataTypes`                  |
| `ClrType`  | `string` | No*      | Direct CLR type override (e.g., `byte`, `string`)                  |

*Each entry must specify either `DataType` or `ClrType` (not both). When `DataType` is used, the full custom type definition (`ClrType`, `MaxLength`, `DefaultValue`) is applied. When `ClrType` is used directly, only the CLR type is overridden.

Project-level mappings take precedence over built-in defaults. Any database type not covered by a project mapping falls through to the built-in default.

**`DbType` matching is case-insensitive** (ordinal). This is necessary because different database providers report the same logical type in different casing (e.g., SQL Server reports `int`, SQLite reports `INTEGER`, PostgreSQL reports `integer`). Auto-populated entries use the casing reported by the provider.

### Custom Data Types

The `DataTypes` section in `persistence.project.json` defines reusable named type definitions. Each specifies the C# type and optional constraints that are applied to Attribute objects before template rendering.

```json
{
  "DataTypes": {
    "Uid": {
      "ClrType": "string",
      "MaxLength": 28,
      "DefaultValue": "Ulid.NewUlid()"
    },
    "Money": {
      "ClrType": "decimal",
      "DefaultValue": "0m"
    }
  }
}
```

| Field          | Type     | Required | Description                                           |
| -------------- | -------- | -------- | ----------------------------------------------------- |
| `ClrType`      | `string` | Yes      | The C# type to use (e.g., `string`, `decimal`, `int`) |
| `MaxLength`    | `int`    | No       | Maximum length constraint, if applicable               |
| `DefaultValue` | `string` | No       | C# expression for the default value (e.g., `Ulid.NewUlid()`, `0m`) |

Custom data types can be referenced from two places:

1. **`TypeMappings`** — automatic mapping from database types (see above)
2. **`Overrides`** — explicit per-property assignment (see section 9)

```json
{
  "Overrides": [
    {
      "Class": "*",
      "Property": "Id",
      "DataType": "Uid"
    }
  ]
}
```

#### Resolution Order

When the server resolves the type for a property, values (`ClrType`, `MaxLength`, `DefaultValue`) **replace** the corresponding fields on the Attribute object before template rendering:

1. **Explicit override** — `DataType` in an override rule takes highest precedence
2. **Project type mapping** — `TypeMappings` matches are applied if no explicit override exists
3. **Built-in type mapping** — the server's default database-to-CLR mapping is used as the baseline

Only the fields specified in the custom data type definition are overridden; unspecified fields retain their introspected values.

#### Example Effect

Given a database column `Id char(28) NOT NULL`, a `TypeMappings` entry mapping `char(28)` to `Uid`, and the `Uid` data type definition above, the Attribute object presented to templates would have:

| Field          | Introspected Value | After Uid Resolution |
| -------------- | ------------------ | ---------------------|
| `DataType`     | `char`             | `char` (unchanged)   |
| `ClrType`      | `string`           | `string`             |
| `MaxLength`    | `28`               | `28`                 |
| `DefaultValue` | `null`             | `Ulid.NewUlid()`     |

This allows `DefaultProperty` (or any variant macro) to generate:

```csharp
public string Id { get; set; } = Ulid.NewUlid();
```

---

## 8. Template Engine

### Liquid Template Context

Templates receive a structured context object. The exact shape depends on the template type.

**Per-entity templates** (executed once per table/view):

| Variable      | Type                | Description                           |
| ------------- | ------------------- | ------------------------------------- |
| `entity`      | Model               | The current table or view (see section 7 for Model type) |
| `schema`      | Schema metadata     | The parent schema (`Name: string`)    |
| `database`    | Database metadata   | Connection-level info: `Database` (name), `Provider` (e.g., `"sqlserver"`) |
| `config`      | Project config      | Values from `persistence.project.json`            |
| `parameters`  | Key-value pairs     | Custom parameters from the tool call  |

**SingleFile scope templates** (executed once for the full matched result set, e.g., DbContext):

| Variable      | Type                | Description                           |
| ------------- | ------------------- | ------------------------------------- |
| `entities`    | Model[]             | All matched tables across all schemas |
| `views`       | Model[]             | All matched views across all schemas  |
| `schemas`     | Schema[] metadata   | All matched schemas (each has `Name: string`) |
| `database`    | Database metadata   | Connection-level info: `Database` (name), `Provider` (e.g., `"sqlserver"`) |
| `config`      | Project config      | Values from `persistence.project.json`            |
| `parameters`  | Key-value pairs     | Custom parameters from the tool call  |

Note: Each entity and view carries its own `Schema` property, so templates can group or filter by schema when needed (e.g., `{% for e in entities %}{% if e.Schema == "dbo" %}...{% endif %}{% endfor %}`).

### Custom Liquid Filters

The template engine provides custom filters useful for code generation:

| Filter          | Example                                                    | Output       |
| --------------- | ---------------------------------------------------------- | ------------ |
| `pascal_case`   | `{{ "order_item" \| pascal_case }}`                        | `OrderItem`  |
| `camel_case`    | `{{ "OrderItem" \| camel_case }}`                          | `orderItem`  |
| `snake_case`    | `{{ "OrderItem" \| snake_case }}`                          | `order_item` |
| `pluralize`     | `{{ "Category" \| pluralize }}`                            | `Categories` |
| `singularize`   | `{{ "Categories" \| singularize }}`                        | `Category`   |
| `type_nullable` | `{{ a.ClrType \| type_nullable: a.IsNullable }}`           | `int?`       |

The `type_nullable` filter appends `?` when `IsNullable` is `true`. For **value types** (`int`, `decimal`, `DateTime`, `Guid`, etc.) this produces the C# nullable value type (`int?`). For **reference types** (`string`, `byte[]`), the `?` produces the nullable reference type annotation (`string?`). When `IsNullable` is `false`, the type is returned unchanged.

### Fluid Configuration

The template engine is configured with strict mode enabled to catch errors early rather than producing silently incorrect output:

| Option | Value | Effect |
|--------|-------|--------|
| `StrictVariables` | `true` | Throws `FluidException` for undefined variables instead of rendering empty string |
| `StrictFilters` | `true` | Throws `FluidException` for unregistered filters instead of passing input through |

Template parsing uses `TryParse` — parse failures return structured error messages (template path, line number, parser message) without throwing exceptions.

### Custom Fluid Tags

The template engine registers two custom tags that are **not** part of standard Liquid or Fluid. Both must be implemented as part of this project:

| Tag | Syntax | Description |
|-----|--------|-------------|
| `{% macro %}` | `{% macro Name(param) %}...{% endmacro %}` | Defines a callable macro function within a template. Macros are registered as `FunctionValue` entries in the template context. The dispatch tag resolves which macro to call based on variant names. |
| `{% dispatch %}` | `{% dispatch object %}` | Takes a Model or Attribute object, resolves its variant for the current artifact via `GetVariant(ArtifactName)`, and calls the matching macro. Falls back to `DefaultClass` (for Models) or `DefaultProperty` (for Attributes) if the macro is not found. See section 9 for full specification. |

Both tags are registered with the `FluidParser` during template engine initialization in `TemplateEngine.cs`.

### Generated File Encoding

All generated files are written as **UTF-8 without BOM** using the operating system's default line endings. This matches the modern .NET SDK default and works correctly across platforms and version control systems.

### Example Templates

**`entity.generated.liquid`** — simple example without variants (always overwritten):

```liquid
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

{%- comment %} Reference navigation properties (many-to-one) {%- endcomment %}
{%- for fk in entity.ForeignKeys %}
    public virtual {{ fk.PrincipalTable | pascal_case | singularize }} {{ fk.PrincipalTable | pascal_case | singularize }} { get; set; } = null!;
{%- endfor %}

{%- comment %} Collection navigation properties (one-to-many) {%- endcomment %}
{%- for ref in entity.ReferencingForeignKeys %}
    public virtual ICollection<{{ ref.Table | pascal_case | singularize }}> {{ ref.Table | pascal_case }} { get; set; } = new List<{{ ref.Table | pascal_case | singularize }}>();
{%- endfor %}
}
```

Note: Even simple templates use `{% dispatch %}` with `DefaultClass`/`DefaultProperty` macros. This ensures variant overrides work seamlessly when added later without template restructuring. See section 9 for the full variant system with custom macros.

**`entity.stub.liquid`** — developer stub (created only once):

```liquid
namespace {{ config.Defaults.Namespace }}.Entities;

public partial class {{ entity.Name | pascal_case }}
{
}
```

**`dbcontext.generated.liquid`** — SingleFile scope example (dispatch works inside entity loops):

```liquid
// <auto-generated>
// This file was generated by pondhawk-mcp. Do not edit manually.
// </auto-generated>

using Microsoft.EntityFrameworkCore;

namespace {{ config.Defaults.Namespace }};

public partial class {{ config.Defaults.ContextName }}DbContext : DbContext
{
{%- for e in entities %}
    public DbSet<{{ e.Name | pascal_case }}> {{ e.Name | pascal_case | pluralize }} { get; set; }
{%- endfor %}
}
```

Note: In SingleFile templates, `ArtifactName` is set to the template key (e.g., `"dbcontext"`). Dispatch calls `GetVariant("dbcontext")` on each object. To assign variants for a SingleFile template, overrides must use the matching artifact key (e.g., `"Artifact": "dbcontext"`). This keeps each template's variant namespace independent.

---

## 9. Variant Override System

This is the most critical architectural feature of pondhawk-mcp. While 95% of entity code generation is standard boilerplate, getting the remaining 5% exactly right is the difference between success and failure. The variant system provides precise, per-class and per-property control over generated code, scoped to individual artifacts.

### Concept

The variant system has three parts:

1. **Overrides** in `persistence.project.json` — assign a variant name to a class or property for a specific artifact
2. **Macros** in Liquid templates — callable Fluid macro functions, one per variant
3. **`{% dispatch %}` tag** — a custom Fluid tag that takes a Model or Attribute object, resolves its variant for the current artifact, and calls the matching macro function, passing the object as the argument

### Override Rules in `persistence.project.json`

Overrides are a flat list of rules. Each rule targets a class (and optionally a property) for a specific artifact, assigning a variant name and/or a custom data type.

```json
{
  "Overrides": [
    {
      "Class": "*",
      "Property": "CreatedAt",
      "Artifact": "entity",
      "Variant": "AuditTimestamp"
    },
    {
      "Class": "Products",
      "Property": "Price",
      "Artifact": "entity",
      "Variant": "Currency"
    },
    {
      "Class": "Products",
      "Property": "Price",
      "Artifact": "dto",
      "Variant": "FormattedCurrency"
    },
    {
      "Class": "Orders",
      "Artifact": "entity",
      "Variant": "SoftDelete"
    }
  ]
}
```

| Field      | Type     | Required | Description                                                     |
| ---------- | -------- | -------- | --------------------------------------------------------------- |
| `Class`    | `string` | Yes      | Exact table/class name or `"*"` for all classes                 |
| `Property` | `string` | No       | Exact column/property name. Omit for class-level variants       |
| `Artifact` | `string` | No       | Template key this override applies to (e.g., `"entity"`, `"dto"`). Required when setting `Variant`. When omitted, `DataType` and `Ignore` apply to **all** artifacts. |
| `Variant`  | `string` | No       | PascalCase variant name that maps to a macro in the template    |
| `DataType` | `string` | No       | Name of a custom data type from the `DataTypes` section. Overrides `ClrType`, `MaxLength`, and `DefaultValue` on the Attribute before rendering (see section 7) |
| `Ignore`   | `bool`   | No       | When `true`, the property is excluded from the model before template rendering. The template never sees it. Default: `false` |

An override must specify at least one of `Variant`, `DataType`, or `Ignore`.

**Key behaviors:**

- `Class` accepts either an exact table/class name or `"*"` to match all classes. No prefix/suffix wildcards.
- `Property` is always an exact column/property name. No wildcards on property. Omit for class-level overrides.
- A property-level override is scoped to a single named property on matched classes
- A class-level override (no `Property`) applies to the class as a whole
- The same class or property can have **different variants for different artifacts** — e.g., `Products.Price` uses `Currency` for the entity template but `FormattedCurrency` for the DTO template
- `Ignore: true` suppresses a property from the model entirely before rendering. When `Artifact` is specified, the property is only ignored for that artifact. When `Artifact` is omitted, the property is ignored for all artifacts. Ignored properties are filtered out of `entity.Attributes` — templates never see them.

#### Override Specificity and Conflict Resolution

When multiple overrides match the same class, property, and artifact, the most specific rule wins.

**Property-level overrides:**

| Priority | Class | Property | Example |
|----------|-------|----------|---------|
| 1 (wins) | Exact | Exact | `Class: "Orders", Property: "CreatedAt"` |
| 2 | `*` | Exact | `Class: "*", Property: "CreatedAt"` |

**Class-level overrides:**

| Priority | Class | Property | Example |
|----------|-------|----------|---------|
| 1 (wins) | Exact | Absent | `Class: "Orders"` |
| 2 | `*` | Absent | `Class: "*"` |

Exact class always beats `*`. If two rules have identical specificity (same class, property, and artifact), the **last entry** in the `Overrides` array wins. This allows a natural layering pattern: broad rules at the top, specific exceptions at the bottom.

**Example:**

```json
"Overrides": [
  { "Class": "*",      "Property": "RowVersion", "Ignore": true },
  { "Class": "*",      "Property": "CreatedAt", "Artifact": "entity", "Variant": "AuditTimestamp" },
  { "Class": "*",      "Property": "UpdatedAt", "Artifact": "entity", "Variant": "AuditTimestamp" },
  { "Class": "Orders", "Property": "CreatedAt", "Artifact": "entity", "Variant": "OrderAudit" },
  { "Class": "Orders", "Property": "InternalNotes", "Artifact": "dto", "Ignore": true }
]
```

- `Products.CreatedAt` for entity → `AuditTimestamp` (rule 2 — wildcard class, only match)
- `Orders.UpdatedAt` for entity → `AuditTimestamp` (rule 3 — wildcard class, only match)
- `Orders.CreatedAt` for entity → `OrderAudit` (rule 4 — exact class beats wildcard class in rule 2)
- `*.RowVersion` for any artifact → ignored, property filtered from model (rule 1)
- `Orders.InternalNotes` for dto → ignored, property filtered from model (rule 5); still visible in entity templates

### Macros in Liquid Templates

Macros are Fluid macro functions defined inline in a Liquid template. Each macro is a callable function that receives the Model or Attribute object as its parameter. The dispatch tag resolves which macro to call based on the variant name.

There are two types of objects that flow through dispatch:

- **Model** — represents a table or view (see section 7). Default fallback macro: `DefaultClass`
- **Attribute** — represents a column/property (see section 7). Default fallback macro: `DefaultProperty`

Views are Models and flow through dispatch identically to tables. The `Class` in overrides can target both table and view names.

```liquid
{%- macro DefaultProperty(a) %}
    public {{ a.ClrType | type_nullable: a.IsNullable }} {{ a.Name | pascal_case }} { get; set; }{% if a.ClrType == "string" and a.IsNullable == false %} = null!;{% endif %}
{%- endmacro %}

{%- macro CurrencyProperty(a) %}
    [Column(TypeName = "decimal(18,2)")]
    [DisplayFormat(DataFormatString = "{0:C}")]
    public decimal {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}

{%- macro AuditTimestampProperty(a) %}
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}
```

**Naming conventions:**

- All macro names use **PascalCase** and must include a **`Class` or `Property` suffix** to avoid name collisions (e.g., `CurrencyProperty`, `SoftDeleteClass`, `AuditableClass`)
- The variant name in `persistence.project.json` must **exactly match** the macro name minus the suffix. The dispatch tag appends the suffix automatically based on the object type:
  - Variant `Currency` on a property → dispatches to `CurrencyProperty`
  - Variant `SoftDelete` on a class → dispatches to `SoftDeleteClass`
- The default fallback macro is `DefaultClass` for class-level dispatch or `DefaultProperty` for property-level dispatch
- Each macro takes a single parameter: the object being dispatched (Model or Attribute)

### Dispatch Tag: `{% dispatch %}`

The custom `{% dispatch %}` Fluid tag is the dispatch mechanism. It:

1. Takes a Model or Attribute object as its argument
2. Reads `ArtifactName` from the template context (set automatically by the rendering engine to the template key, e.g., `"entity"`, `"dto"`, `"dbcontext"`)
3. Calls `GetVariant(artifactName)` on the object to resolve the PascalCase variant name for the current artifact
4. Appends a type suffix based on the object's runtime type: `Class` for Models, `Property` for Attributes
5. Finds the macro function matching the full name (e.g., `CurrencyProperty`, `SoftDeleteClass`) and invokes it, passing the object as the argument
6. If the variant is empty or no matching macro exists, falls back to `DefaultClass` (for Models) or `DefaultProperty` (for Attributes)
7. If no matching macro is found at all, writes an error comment: `/* dispatch error: macro 'X' not found */`

**Syntax:**

```liquid
{% dispatch entity %}
{% dispatch a %}
```

### Complete Template Example

**`entity.generated.liquid`** — with variant support:

```liquid
// <auto-generated>
// This file was generated by pondhawk-mcp. Do not edit manually.
// Any changes will be overwritten on next generation.
// </auto-generated>

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace {{ config.Defaults.Namespace }}.Entities;

{%- macro DefaultClass(m) %}
public partial class {{ m.Name | pascal_case }}
{%- endmacro %}

{%- macro SoftDeleteClass(m) %}
public partial class {{ m.Name | pascal_case }} : ISoftDeletable
{%- endmacro %}

{% dispatch entity %}
{

{%- macro DefaultProperty(a) %}
    public {{ a.ClrType | type_nullable: a.IsNullable }} {{ a.Name | pascal_case }} { get; set; }{% if a.ClrType == "string" and a.IsNullable == false %} = null!;{% endif %}
{%- endmacro %}

{%- macro CurrencyProperty(a) %}
    [Column(TypeName = "decimal(18,2)")]
    public decimal {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}

{%- macro AuditTimestampProperty(a) %}
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}

{%- for a in entity.Attributes %}
{% dispatch a %}
{%- endfor %}

{%- comment %} Reference navigation properties (many-to-one) {%- endcomment %}
{%- for fk in entity.ForeignKeys %}
    public virtual {{ fk.PrincipalTable | pascal_case | singularize }} {{ fk.PrincipalTable | pascal_case | singularize }} { get; set; } = null!;
{%- endfor %}

{%- comment %} Collection navigation properties (one-to-many) {%- endcomment %}
{%- for ref in entity.ReferencingForeignKeys %}
    public virtual ICollection<{{ ref.Table | pascal_case | singularize }}> {{ ref.Table | pascal_case }} { get; set; } = new List<{{ ref.Table | pascal_case | singularize }}>();
{%- endfor %}
}
```

### How the AI Agent Uses This

When an AI agent (Claude) needs to customize code generation for a specific class or property, the workflow is:

1. **Add an override** to `persistence.project.json` — assign a PascalCase variant name to the target class/property for the relevant artifact (e.g., `"Variant": "Currency"`)
2. **Add a macro function** to the Liquid template — define a `{% macro VariantSuffix(obj) %}...{% endmacro %}` where the name is the variant + `Class` or `Property` suffix (e.g., `CurrencyProperty`, `SoftDeleteClass`)
3. **Regenerate** — the `{% dispatch entity %}` or `{% dispatch a %}` tag automatically resolves the variant via `GetVariant(artifactName)`, appends the type suffix, and calls the matching macro

This approach means the AI agent never needs to hand-edit generated `.generated.cs` files. All customization flows through the declarative override rules in `persistence.project.json` and the macro functions in the Liquid templates.

#### Inferring Relationships from Naming Conventions

For databases without FK constraints, the AI agent can infer relationships from naming conventions:

1. **Read `conventions.md`** — an optional project file where developers describe their database naming conventions in natural language (e.g., "Foreign key columns use `{SingularTableName}Id`", "Self-referencing columns use a role prefix like `ParentCategoryId`", "The `StatusCode` column on Orders references `OrderStatuses.Code`")
2. **Call `introspect_schema`** — get the raw schema with column names and types
3. **Apply conventions intelligently** — match column names against the described patterns, handling exceptions and ambiguities that no regex pattern could
4. **Write inferred relationships** to the `Relationships` section of `persistence.project.json` — making them explicit, auditable, and version-controlled
5. **Ask the developer** when encountering ambiguity (e.g., "Is `StatusId` on `Orders` a FK to `Statuses`, or just an enum value?")

This keeps the server simple (no convention inference logic) while being far more capable than any hardcoded pattern matching. The AI understands natural language rules, handles edge cases, and produces explicit declarations that are reviewable in the project file.

### AI Agent Instructions (`AGENTS.md`)

The MCP server exposes tools, but an AI agent also needs to understand how to edit `persistence.project.json` and author Liquid templates. This knowledge is provided through two channels:

#### 1. `AGENTS.md` — Generated by `init`

The `init` tool creates an `AGENTS.md` file in the project directory. This file is the primary reference for AI agents working with the project. It must cover:

- **Available MCP tools** and when to use each one (`init`, `introspect_schema`, `generate`, `generate_ddl`, `generate_diagram`, `list_templates`, `validate_config`, `update`)
- **`persistence.project.json` structure** — all sections (`Connection`, `OutputDir`, `DataTypes`, `TypeMappings`, `Templates`, `Defaults`, `Relationships`, `Overrides`) with field descriptions and examples
- **`db-design.json`** — written by `introspect_schema` (database-first) or hand-authored by the AI agent (design-first); read by `generate`, `generate_ddl`, and `generate_diagram`; version-controlled; the AI agent should read this file directly when it needs schema details rather than re-introspecting
- **Design-first schema authoring** — how to create `db-design.json` by hand for new databases:
  - Always set `"Origin": "design"` to prevent `introspect_schema` from overwriting the file
  - Required fields: `Origin`, `Schemas[].Name`, `Tables[].Name`, `Columns[].Name`, `Columns[].DataType`
  - Optional fields: `ClrType` (not needed for DDL), `ReferencingForeignKeys` (computed automatically), `Provider`/`Database` (not needed for DDL)
  - Enum definitions: `Enums[]` with `Name` and `Values[]` for type-safe columns
  - Notes: `Note` on tables and columns for documentation (emitted as SQL comments in DDL)
  - FK definitions: `ForeignKeys[]` with `OnDelete` and optional `OnUpdate`; constraint names auto-generated if omitted
  - `$schema` reference to `db-design.schema.json` for IDE autocompletion
- **`generate_ddl` usage** — generate dialect-specific DDL SQL from `db-design.json`; requires `provider` parameter; output defaults to `db-design.{provider}.sql`
- **`generate_diagram` usage** — generate interactive HTML ER diagram from `db-design.json`; output defaults to `db-design.html`; open in browser for visual review
- **Design-first workflow examples:**
  - Design a new database: write `db-design.json` (with `"Origin": "design"`) → `generate_ddl` → review SQL → deploy
  - Visualize a schema: write or introspect `db-design.json` → `generate_diagram` → open HTML in browser
  - Design, review, and deploy: write `db-design.json` → `generate_diagram` (visual check) → `generate_ddl` → deploy
- **`Origin` field** — `"introspected"` (written by `introspect_schema`, can be overwritten) vs `"design"` (written by AI, protected from overwrite); to switch modes, delete `db-design.json` or change `Origin`
- **Template authoring** — Liquid syntax basics, available custom filters (`pascal_case`, `camel_case`, etc.), template scope (`PerModel` vs `SingleFile`), template mode (`Always` vs `SkipExisting`)
- **The variant/override/dispatch workflow**:
  1. Add an override to `persistence.project.json` with a PascalCase `Variant` name
  2. Add a matching macro in the template (`{VariantName}Class` or `{VariantName}Property`)
  3. The `{% dispatch %}` tag automatically resolves variants and calls macros
  4. `DefaultClass` and `DefaultProperty` are the fallback macros
- **Macro naming conventions** — PascalCase, `Class` suffix for Models, `Property` suffix for Attributes, exact match between variant name and macro prefix
- **Creating new templates** — how to add a template entry to `persistence.project.json`, wire up the `Path`, `OutputPattern`, `Scope`, and `Mode`, and write the Liquid template file
- **Override rules** — `Class` accepts exact name or `"*"`, `Property` is always exact, `Ignore: true` to suppress properties, specificity rules, last-entry-wins for ties
- **Custom data types** — how to define them in `DataTypes`, reference them in `TypeMappings` and `Overrides`
- **Relationships** — how to add explicit relationships for databases without FK constraints
- **`conventions.md`** — optional file describing naming conventions for the AI agent to infer relationships; not processed by the server, purely for AI consumption
- **Environment variables** — `${VAR}` substitution, `.env` file usage
- **Validation** — always run `validate_config` after editing the project file

`AGENTS.md` is committed to version control alongside the project. Teams can customize it to add project-specific conventions or workflows.

#### 2. MCP Tool Descriptions

Each MCP tool registration includes a description that provides basic guidance and references `AGENTS.md` for details. For example, the `generate` tool description would include:

> Generates code by rendering Liquid templates against introspected schema data. See AGENTS.md in the project directory for instructions on configuring templates, overrides, and variants.

This ensures that even if an AI agent hasn't read `AGENTS.md` yet, the tool descriptions point it in the right direction.

---

## 10. Filtering

Table and view filtering supports glob-style wildcard patterns applied at both the project configuration level and per tool invocation.

### Pattern Syntax

| Pattern    | Matches                                |
| ---------- | -------------------------------------- |
| `Products` | Exact match on `Products`              |
| `Order*`   | `Orders`, `OrderItems`, `OrderHistory` |
| `*Log`     | `AuditLog`, `ErrorLog`                 |
| `*`        | All tables/views                       |

### Precedence

1. For `introspect_schema`, tool parameter `include`/`exclude` overrides project config `Defaults.Include`/`Defaults.Exclude`. For `generate`, the `models` parameter (exact names) overrides `Include`/`Exclude` entirely when provided.
2. `exclude` takes precedence over `include`
3. If no `include` is specified, all tables/views are included
4. System tables (e.g., `__EFMigrationsHistory`) are excluded by default

### Tool-Specific Filtering

| Tool | Filtering Mechanism |
|------|-------------------|
| `introspect_schema` | `include`/`exclude` parameters (wildcard patterns), `schemas` parameter, `includeViews` parameter |
| `generate` | `models` parameter (exact names, no wildcards — completely overrides `Include`/`Exclude` when provided), `schemas` parameter, `includeViews` parameter. Config-level `Include`/`Exclude` apply when `models` is omitted. |

---

## 11. Acceptance Criteria

### Schema Introspection

- [ ] Introspects tables, views, columns, primary keys, foreign keys, and indexes from SQL Server (including Azure SQL)
- [ ] Introspects tables, views, columns, primary keys, foreign keys, and indexes from PostgreSQL
- [ ] Introspects tables, views, columns, primary keys, foreign keys, and indexes from MySQL
- [ ] Introspects tables, views, columns, primary keys, foreign keys, and indexes from MariaDB
- [ ] Introspects tables, views, columns, primary keys, foreign keys, and indexes from SQLite
- [ ] Maps native database types to .NET CLR types correctly for each provider
- [ ] Tables are mapped to Model objects with `IsView` = `false`; columns are mapped to Attribute objects
- [ ] Views are mapped to Model objects with `IsView` = `true`; view columns are mapped to Attribute objects
- [ ] Model exposes `GetVariant(artifactName)` for variant resolution by the dispatch tag
- [ ] Attribute exposes `GetVariant(artifactName)` for variant resolution by the dispatch tag
- [ ] Applies include/exclude wildcard filtering to tables and views
- [ ] Writes the introspected schema metadata to `db-design.json` in the project directory with `"Origin": "introspected"` (explicit `Relationships` from config are not included)
- [ ] If `db-design.json` exists with `"Origin": "design"`, `introspect_schema` returns an error and refuses to overwrite
- [ ] If `db-design.json` exists with `"Origin": "introspected"` (or no `Origin` field), `introspect_schema` overwrites it normally
- [ ] Returns a lightweight summary (table/view names, column counts, schema file path) via the MCP tool result — not the full schema
- [ ] `db-design.json` is a version-controlled project artifact that contains only what is actually in the database and enables generation without a live database connection

### Code Generation and File Writing

- [ ] Reads `persistence.project.json` and referenced Liquid templates from the target project path
- [ ] Templates with `Scope: "PerModel"` are rendered once per matched table/view with `entity`, `schema`, `database`, `config`, and `parameters` in the context
- [ ] Templates with `Scope: "SingleFile"` are rendered once for the full matched result set with `entities`, `views`, `schemas`, `database`, `config`, and `parameters` in the context
- [ ] The `database` context variable contains `Database` (name) and `Provider` (e.g., `"sqlserver"`)
- [ ] The `config` context variable contains values from `persistence.project.json` (e.g., `config.Defaults.Namespace`)
- [ ] `entity.Schema` is available in OutputPattern for multi-schema output path disambiguation
- [ ] `validate_config` warns if multiple entities would resolve to the same output path
- [ ] Writes generated files to the configured output directory
- [ ] Templates with `Mode: "Always"` overwrite existing files on every run
- [ ] Templates with `Mode: "SkipExisting"` only create files that don't already exist
- [ ] Reports file-level results (created, overwritten, skipped) in the tool response
- [ ] The `generate` tool's `templates` parameter selectively runs only the specified template keys (default: all)
- [ ] The `generate` tool's `models` parameter filters generation to exact table/view names (no wildcards), completely overriding config `Include`/`Exclude`
- [ ] The `generate` tool's `schemas` parameter filters generation to the specified schemas
- [ ] The `generate` tool's `includeViews` parameter overrides `Defaults.IncludeViews`
- [ ] The `generate` tool reads schema data from `db-design.json` — no database connection is required
- [ ] The `generate` tool returns an error if `db-design.json` does not exist, instructing the agent to run `introspect_schema` first
- [ ] Supports custom `parameters` passed through to the template context
- [ ] Custom Liquid filters (`pascal_case`, `camel_case`, `snake_case`, `pluralize`, `singularize`, `type_nullable`) work correctly
- [ ] Generated files are written as UTF-8 without BOM using OS-default line endings

### Variant Override System

- [ ] Overrides in `persistence.project.json` are parsed and matched to classes/properties
- [ ] `Class: "*"` matches all classes; `Class` with an exact name matches only that class
- [ ] `Property` is always an exact name (no wildcards); absent `Property` indicates a class-level override
- [ ] Exact class overrides take precedence over `*` wildcard class overrides for the same property and artifact
- [ ] When two rules have identical specificity (same class, property, artifact), the last entry in the array wins
- [ ] The same class/property can have different variants for different artifacts
- [ ] `Ignore: true` with `Artifact` specified filters the property from `entity.Attributes` only for that artifact's template
- [ ] `Ignore: true` without `Artifact` filters the property from `entity.Attributes` for all templates
- [ ] Ignored properties are removed from the model before rendering — templates never see them
- [ ] Macros defined as Fluid functions (`{% macro VariantSuffix(obj) %}...{% endmacro %}`) are callable by the dispatch tag
- [ ] `{% dispatch entity %}` resolves the Model's variant via `GetVariant(artifactName)`, appends `Class` suffix, and calls the matching macro (e.g., `SoftDeleteClass`)
- [ ] `{% dispatch a %}` resolves the Attribute's variant via `GetVariant(artifactName)`, appends `Property` suffix, and calls the matching macro (e.g., `CurrencyProperty`)
- [ ] Variant names in `persistence.project.json` use PascalCase and exactly match the macro name prefix (e.g., variant `Currency` → macro `CurrencyProperty`)
- [ ] `ArtifactName` is automatically set in the template context to the template key (e.g., `"entity"`, `"dto"`, `"dbcontext"`)
- [ ] `{% dispatch %}` uses the object's runtime type (Model vs Attribute) to determine the suffix — not a property check
- [ ] `{% dispatch %}` on a view Model behaves identically to a table Model (appends `Class`, falls back to `DefaultClass`)
- [ ] `{% dispatch %}` works inside loops in SingleFile templates (e.g., `{% for e in entities %}{% dispatch e %}{% endfor %}`)
- [ ] Overrides targeting a SingleFile template use the template key as `Artifact` (e.g., `"Artifact": "dbcontext"`)
- [ ] `{% dispatch %}` falls back to `DefaultClass` for Model objects when no variant is assigned
- [ ] `{% dispatch %}` falls back to `DefaultProperty` for Attribute objects when no variant is assigned
- [ ] `{% dispatch %}` writes an error comment (`/* dispatch error: macro 'X' not found */`) when no matching macro exists (including no default)
- [ ] `Class` in overrides can target both table and view names

### Relationships and Navigation Properties

- [ ] Introspected FK constraints are exposed as `ForeignKeys[]` on each table in `db-design.json`
- [ ] Explicit `Relationships` in `persistence.project.json` are merged with introspected FKs at generation time (not written to `db-design.json`)
- [ ] `ReferencingForeignKeys[]` is populated at generation time on each table with the inverse of all FK relationships (both introspected and explicit)
- [ ] Explicit relationships take precedence over introspected FKs when both match (same dependent table, schema, and columns)
- [ ] Explicit relationships support `DependentSchema` and `PrincipalSchema` fields, defaulting to `Defaults.Schema` when omitted
- [ ] Explicit relationships for databases without FK constraints produce identical `ForeignKeys[]` / `ReferencingForeignKeys[]` entries as introspected ones
- [ ] Changes to `Relationships` in `persistence.project.json` take effect on the next `generate` call without re-running `introspect_schema`
- [ ] Templates can iterate `entity.ForeignKeys` to generate reference navigation properties (many-to-one)
- [ ] Templates can iterate `entity.ReferencingForeignKeys` to generate collection navigation properties (one-to-many)
- [ ] Invalid relationship definitions (referencing non-existent tables or columns) produce clear validation errors

### Type Mapping and Custom Data Types

- [ ] Custom data types defined in `DataTypes` are parsed from `persistence.project.json`
- [ ] `TypeMappings` entries targeting a `DataType` apply the full custom type definition (`ClrType`, `MaxLength`, `DefaultValue`)
- [ ] `TypeMappings` entries targeting a `ClrType` directly override only the CLR type
- [ ] Project-level `TypeMappings` take precedence over built-in default mappings
- [ ] Database types not covered by project mappings fall through to built-in defaults
- [ ] Explicit `DataType` in an override rule takes highest precedence over `TypeMappings`
- [ ] Only fields specified in the custom data type definition are overridden; unspecified fields retain introspected values
- [ ] Invalid `DataType` references (not defined in `DataTypes`) produce a clear validation error
- [ ] `introspect_schema` auto-populates `TypeMappings` with newly discovered database types and their default CLR mappings
- [ ] Auto-population is add-only and idempotent — existing entries are never modified or removed, repeated introspections do not create duplicates
- [ ] The written config file remains valid JSON after TypeMappings auto-population
- [ ] After TypeMappings write-back, the config cache timestamp is updated without invalidating the schema cache
- [ ] `DbType` matching is case-insensitive (ordinal)

### Configuration

- [ ] `validate_config` validates `persistence.project.json` and reports errors/warnings without connecting to any database
- [ ] `list_templates` returns all template entries with artifact key, Path, OutputPattern, Scope, Mode, and AppliesTo
- [ ] All configuration (connection, templates, data types, overrides, filtering) is read from `persistence.project.json`
- [ ] The MCP server accepts a `--project` argument at startup specifying the project root path
- [ ] The server is scoped to a single project with a single database connection per instance
- [ ] `${VARIABLE_NAME}` syntax in connection strings is resolved from environment variables at runtime
- [ ] The server loads a `.env` file from the project directory if present
- [ ] System environment variables take precedence over `.env` values
- [ ] Missing environment variables produce a clear error when the value is needed

### Init Tool

- [ ] `init` creates `persistence.project.json` with a valid skeleton configuration
- [ ] `init` creates `persistence.project.schema.json` with the JSON Schema for IDE autocompletion and validation
- [ ] `init` creates `db-design.schema.json` with the JSON Schema for `db-design.json` validation and IDE autocompletion (see section 16.2.1)
- [ ] `init` creates `AGENTS.md` with comprehensive instructions covering all topics listed in section 9, subsection "AI Agent Instructions (`AGENTS.md`)"
- [ ] `init` creates `.env` — if `connectionString` is provided, writes the real value so `introspect_schema` works immediately; otherwise writes a provider-specific placeholder (skipped if `.env` already exists)
- [ ] `init` creates `templates/entity.generated.liquid` with working dispatch macros, FK navigation, and collection navigation
- [ ] `init` creates `templates/entity.stub.liquid` with a minimal SkipExisting stub
- [ ] `init` returns an error if `persistence.project.json` already exists (never overwrites)
- [ ] Created templates are functional — generating against a real schema produces valid C# code
- [ ] MCP tool descriptions reference `AGENTS.md` for detailed usage instructions

### Update Tool

- [ ] `update` returns an error if `persistence.project.json` does not exist, instructing the user to run `init` first
- [ ] `update` overwrites `AGENTS.md` with the latest embedded instructions
- [ ] `update` overwrites `persistence.project.schema.json` with the latest JSON Schema
- [ ] `update` overwrites `db-design.schema.json` with the latest JSON Schema for `db-design.json`
- [ ] `update` normalizes `persistence.project.json` via a load/save round-trip
- [ ] `update` preserves all existing configuration values (Templates, TypeMappings, Overrides, Relationships, DataTypes, Logging) during normalization
- [ ] `update` updates the config cache timestamp after write-back to avoid unnecessary invalidation
- [ ] `update` returns the list of updated files and a confirmation message

### Template AppliesTo Filtering

- [ ] Templates with `AppliesTo: "Tables"` only run for table models (not views)
- [ ] Templates with `AppliesTo: "Views"` only run for view models (not tables)
- [ ] Templates with `AppliesTo: "All"` (or `AppliesTo` omitted) run for both tables and views
- [ ] `AppliesTo` is an optional field that defaults to `All` when not specified

### Empty Output Skipping

- [ ] Templates that render to whitespace-only output do not write a file to disk
- [ ] Skipped empty files are reported in the `generate` response

### Error Handling

- [ ] All tool call failures return MCP error responses with `isError: true`; the server process never crashes
- [ ] Missing `--project` argument or non-existent project path causes the server to exit with non-zero code at startup
- [ ] Missing `persistence.project.json` allows the server to start (so `init` can be called) but other tools return config errors
- [ ] Malformed JSON in `persistence.project.json` returns an error with parse location (line/column)
- [ ] Database connection failures include provider and host but never expose credentials
- [ ] Missing `db-design.json` when `generate` is called returns an error instructing to run `introspect_schema` first
- [ ] Unresolved `${VAR}` references in connection strings return an error naming the variable
- [ ] Template parse errors (Liquid syntax) return the template path, line number, and parser message via `TryParse`
- [ ] Fluid is configured with `StrictVariables = true` and `StrictFilters = true`
- [ ] Undefined variable references in templates throw `FluidException` with the template path and variable name
- [ ] Unregistered filters in templates throw `FluidException` with the template path and filter name
- [ ] Rendering exceptions are caught per-file and reported with template path, entity name, and exception message
- [ ] Partial generation failures report per-file results (successes and failures) without rolling back
- [ ] Output directories are created automatically if they don't exist
- [ ] `validate_config` catches configuration errors before generation is attempted

### MCP Protocol and Server Lifecycle

- [ ] Server communicates via stdio transport
- [ ] Server runs as a long-lived process for the duration of the MCP client session
- [ ] All 8 tools (`init`, `introspect_schema`, `generate`, `generate_ddl`, `generate_diagram`, `list_templates`, `validate_config`, `update`) are discoverable via MCP tool listing
- [ ] Tool parameters are validated and return clear error messages for invalid input
- [ ] `introspect_schema` writes introspected schema (database contents only, no explicit relationships) to `db-design.json` and returns a lightweight summary — large schemas never flood the MCP response
- [ ] Project configuration is cached after first load and reused across tool calls
- [ ] Compiled Liquid templates are cached after first parse and reused across tool calls
- [ ] Schema from `db-design.json` is cached after first load and reused across tool calls
- [ ] Before each tool call, cached project configuration is invalidated if the file's last-modified timestamp has changed
- [ ] Before each tool call, cached templates are invalidated if the `.liquid` file's last-modified timestamp has changed
- [ ] Before each tool call, cached schema is invalidated if `db-design.json`'s last-modified timestamp has changed
- [ ] When `persistence.project.json` changes, config and template caches are invalidated

### Logging

- [ ] When `Logging.Enabled` is `false` (or the `Logging` section is absent), no log file is created and logging has negligible overhead
- [ ] When `Logging.Enabled` is `true`, a log file is created at the configured `LogPath`
- [ ] Serilog is registered as the `Microsoft.Extensions.Logging` provider so that .NET runtime, MCP SDK, DatabaseSchemaReader, Fluid, and all third-party package log output is captured
- [ ] Log level filtering respects the configured `Level` (e.g., `Information` suppresses `Debug` and `Verbose` entries)
- [ ] Rolling interval creates new log files at the configured boundary (e.g., daily)
- [ ] `RetainedFileCountLimit` deletes log files beyond the configured count
- [ ] Connection strings and credentials are never written to log files
- [ ] Tool calls are logged with tool name, parameters (redacted), duration, and success/failure status
- [ ] Schema introspection logs include provider, table/view counts, type mappings applied, and `db-design.json` write
- [ ] Code generation logs include explicit relationship merge results (relationships applied, skipped)
- [ ] Template rendering logs include template path, artifact name, models rendered, and dispatch decisions
- [ ] Errors are logged with full exception details including stack traces
- [ ] Logging configuration changes take effect on next tool call (follows the config cache invalidation rules)

### JSON Schema Validation (db-design.json)

- [ ] `db-design.schema.json` is generated by `init` alongside `persistence.project.schema.json`
- [ ] `db-design.schema.json` is refreshed by `update` to pick up new schema extensions
- [ ] `db-design.json` includes a `$schema` reference (`"$schema": "db-design.schema.json"`) for IDE autocompletion
- [ ] `introspect_schema` writes the `$schema` field when generating `db-design.json`
- [ ] `generate_ddl` validates `db-design.json` against the JSON Schema before generating DDL; returns structured errors on failure
- [ ] `generate_diagram` validates `db-design.json` against the JSON Schema before generating a diagram; returns structured errors on failure
- [ ] `validate_config` validates `db-design.json` against the JSON Schema if it exists (optional, since db-design.json may not exist yet)
- [ ] The JSON Schema uses `required` only for fields that must always be present (`Origin`, table Name, column Name, column DataType)
- [ ] `Origin` is validated against the enum `introspected`, `design`
- [ ] Introspection-specific fields (`ClrType`, `ReferencingForeignKeys`) are optional in the JSON Schema
- [ ] `OnDelete`/`OnUpdate` values are validated against `Cascade`, `SetNull`, `SetDefault`, `NoAction`, `Restrict`
- [ ] Validation errors include field paths and violation descriptions

### DDL Generation

- [ ] `generate_ddl` generates dialect-specific DDL SQL from `db-design.json`
- [ ] DDL includes CREATE TABLE with columns, data types, NOT NULL, DEFAULT, and auto-increment per dialect
- [ ] DDL includes primary key constraints
- [ ] DDL includes UNIQUE constraints
- [ ] DDL includes indexes as `CREATE INDEX` statements
- [ ] DDL includes foreign keys as `ALTER TABLE ADD CONSTRAINT FOREIGN KEY` with ON DELETE and ON UPDATE
- [ ] DDL handles enum types per dialect: PostgreSQL uses `CREATE TYPE AS ENUM`, SQL Server/SQLite use CHECK constraints, MySQL uses inline `ENUM(...)`
- [ ] DDL type mapping covers ~28 generic types mapped to dialect-specific SQL types
- [ ] Unrecognized types are passed through verbatim
- [ ] Precision/scale is preserved on parameterized types
- [ ] Tables are output in dependency order (topological sort on FK references) to avoid forward references
- [ ] Foreign keys are output as separate `ALTER TABLE` statements after all CREATE TABLE statements
- [ ] Constraint/index names are auto-generated (`PK_{table}`, `FK_{table}_{ref}`, `IX_{table}_{cols}`) when null in db-design.json
- [ ] Notes are emitted as SQL comments in the DDL output
- [ ] Empty schema generates empty DDL with a header comment and a warning
- [ ] Circular FK references are handled correctly (deferred FK creation via ALTER TABLE)
- [ ] SQL Server dialect uses `[bracket]` quoting, `IDENTITY(1,1)`, CHECK for enums, `bit` for boolean
- [ ] PostgreSQL dialect uses `"double-quote"` quoting, `GENERATED ALWAYS AS IDENTITY`, `CREATE TYPE AS ENUM`, `boolean`
- [ ] MySQL dialect uses `` `backtick` `` quoting, `AUTO_INCREMENT`, inline `ENUM(...)`, `tinyint(1)` for boolean, `ENGINE = INNODB`
- [ ] SQLite dialect uses `"double-quote"` quoting, `AUTOINCREMENT`, CHECK for enums, `INTEGER` for boolean
- [ ] If `persistence.project.json` exists and has explicit `Relationships`, they are merged with db-design.json ForeignKeys
- [ ] `generate_ddl` works without `persistence.project.json` existing
- [ ] DDL output is written as UTF-8 without BOM

### HTML ER Diagram Generation

- [ ] `generate_diagram` generates a single self-contained HTML file with embedded CSS, JS, and SVG
- [ ] Tables are rendered as styled boxes with header (table name) and rows (column name, type, constraint icons for PK/FK/unique/not-null)
- [ ] FK relationships are rendered as SVG arrows/lines between connected tables
- [ ] Enum types are shown as distinct boxes
- [ ] Color coding distinguishes PK columns, FK columns, and nullable vs not-null
- [ ] Interactive pan (click-drag on background) works
- [ ] Interactive zoom (scroll wheel) works
- [ ] Tables can be dragged to reposition
- [ ] Hovering on an FK line highlights the relationship
- [ ] Auto-layout uses topological sort + grid placement with FK-connected tables placed adjacent
- [ ] No external JS library dependencies — vanilla JS with SVG rendering
- [ ] If `persistence.project.json` exists and has explicit `Relationships`, they are merged with db-design.json ForeignKeys
- [ ] `generate_diagram` works without `persistence.project.json` existing
- [ ] Title bar displays project name, description, and generation date
- [ ] Title bar shows "ER Diagram" when no `ProjectName` is configured
- [ ] Sidebar header reads "Entities"
- [ ] Zoom toolbar provides +/−, zoom level display, and Fit button
- [ ] Search box filters entities by name and zooms to selected result
- [ ] Clicking an entity in All view navigates to its FK-chain group detail view
- [ ] All view uses group-sorted grid layout without relationship lines
- [ ] All view preserves zoom/pan state when switching to a group and back

### DDL and Diagram MCP Tools

- [ ] `generate_ddl` requires the `provider` parameter
- [ ] `generate_ddl` defaults output to `{ProjectName}.{provider}.sql` (or `db-design.{provider}.sql` when `ProjectName` is not set)
- [ ] `generate_ddl` returns a summary with table, enum, index, and FK counts plus the output path
- [ ] `generate_diagram` defaults output to `{ProjectName}.html` (or `db-design.html` when `ProjectName` is not set)
- [ ] `generate_diagram` returns a summary with table and relationship counts plus the output path
- [ ] Both tools return an error if `db-design.json` doesn't exist
- [ ] Both tools work with any db-design.json — whether hand-designed by Claude or introspected from a database
- [ ] Invalid provider for `generate_ddl` returns an error listing valid values

### Testing

- [ ] All tests run without external database servers — SQLite in-memory databases are used for introspection and pipeline tests
- [ ] Configuration tests cover JSON parsing, validation errors, and all field types
- [ ] Environment resolver tests cover `${VAR}` substitution, `.env` loading, and precedence
- [ ] Schema introspection tests use real SQLite databases to verify tables, views, columns, PKs, FKs, indexes, and filtering
- [ ] Type mapper tests cover built-in defaults for all 4 ADO.NET providers with 5 logical database targets (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite) and project-level overrides, plus SQLite-backed auto-population of TypeMappings
- [ ] Override resolver tests cover exact class, wildcard class, specificity ranking, last-wins ties, Ignore, and per-artifact variants
- [ ] Relationship merger tests cover explicit + introspected FK merging, schema defaults, and ReferencingForeignKeys population, plus SQLite-backed merge of real FKs with explicit relationships
- [ ] Dispatch tag tests cover Model → Class, Attribute → Property, fallbacks, error comments, views, and SingleFile loops
- [ ] Custom filter tests cover all 6 filters with edge cases
- [ ] Template rendering tests cover PerModel and SingleFile scopes with full context verification
- [ ] File writer tests cover Always/SkipExisting modes, directory creation, UTF-8 encoding, and per-file action reporting
- [ ] DDL generator tests cover per-dialect full output verification for all 4 dialects (SQL Server, PostgreSQL, MySQL, SQLite)
- [ ] DDL type mapper tests cover all 28+ generic types across 4 dialects, passthrough for unrecognized types, and precision preservation
- [ ] Diagram generator tests verify HTML output contains expected tables, relationships, and is valid HTML
- [ ] Cache tests cover hit/miss, timestamp invalidation, and cascade invalidation
- [ ] Logging tests cover enabled/disabled toggle, MEL interception, level filtering, credential redaction, and no-op logger when disabled
- [ ] End-to-end pipeline tests use real SQLite databases through the full introspect → override → render → verify chain
- [ ] MCP integration tests cover init, generate, generate_ddl, generate_diagram, validate_config, and update tool behavior

### Build System

- [ ] `dotnet run --project build -- --target Clean` deletes `bin/`, `obj/`, and `publish/` directories
- [ ] `dotnet run --project build -- --target Build` compiles the solution in Release configuration
- [ ] `dotnet run --project build -- --target Test` runs all tests in both test projects and fails the build on any test failure
- [ ] `dotnet run --project build -- --target Publish` produces self-contained single-file executables for all 4 platforms (`win-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`)
- [ ] Running the build with no target argument executes the default task chain: Clean → Restore → Build → Test
- [ ] Published binaries are self-contained (no .NET runtime required on target machine)
- [ ] Published binaries are single-file executables (no loose DLLs alongside the binary)
- [ ] Published binaries run correctly on their target platform (stdio MCP transport works, database providers load, templates render)

---

## 12. Testing Strategy

### Technology

| Component      | Technology                           |
|----------------|--------------------------------------|
| Test Framework | xUnit (latest stable)                |
| Assertions     | Shouldly (latest stable)             |
| Mocking        | NSubstitute (latest stable)          |
| Test Database  | Microsoft.Data.Sqlite (latest stable) — SQLite in-memory databases |

All tests run without external dependencies (no external database servers required). Where tests need a real database (introspection, type mapping, relationship merging, end-to-end pipeline), they use **SQLite in-memory databases** (`Data Source=:memory:`) created and torn down per test. This exercises the real DatabaseSchemaReader and ADO.NET code paths rather than mocking them, providing significantly higher confidence. Pure logic tests (override resolution, variant resolution, template rendering, config parsing) continue to use mocked inputs where no database is involved.

### `Pondhawk.Persistence.Core.Tests` — Unit Tests

#### Configuration

| Test Class | Covers |
|------------|--------|
| `ProjectConfigurationTests` | JSON parsing, all sections deserialized correctly, PascalCase field mapping, missing required fields produce errors, malformed JSON reports line/column, `Logging` section deserialized with defaults when absent |
| `EnvironmentResolverTests` | `${VAR}` substitution from system env vars, `.env` file loading, system env overrides `.env`, unresolved variables produce clear errors, only connection strings are resolved (not OutputDir/template paths/log path), `.env` parse errors produce warnings |
| `ConfigurationValidatorTests` | Missing template files, invalid `DataType` references in overrides, invalid `Relationships` (non-existent tables/columns), duplicate template keys, output path collision detection, invalid `Logging.Level` value, invalid `Logging.RollingInterval` value, invalid `Provider` value, invalid `Scope` value, invalid `Mode` value, missing required sections (`Connection`, `OutputDir`, `Templates`), missing required `Connection` fields (`Provider`, `ConnectionString`), missing required template fields (`Path`, `OutputPattern`, `Scope`, `Mode`), template files parsed with `TryParse` during validation |

#### Schema Introspection (SQLite-backed)

| Test Class | Covers |
|------------|--------|
| `SchemaIntrospectorTests` | **Uses SQLite in-memory DB.** Introspects tables with various column types (INTEGER, TEXT, REAL, BLOB, NUMERIC), primary keys (single and composite), nullable/non-nullable columns, default values, foreign key constraints (single and composite), indexes (unique and non-unique), views (with `IsView=true`), tables with no FK constraints, empty database (no tables), `Include`/`Exclude` filtering applied during introspection, `IncludeViews=false` excludes views, `IncludeViews=true` includes views, resulting Model/Attribute objects have correct property values, `ForeignKeys[]` and `ReferencingForeignKeys[]` populated correctly from real FK constraints |

#### Type Mapping

| Test Class | Covers |
|------------|--------|
| `TypeMapperTests` | Built-in defaults for each provider (SQL Server, PostgreSQL, MySQL/MariaDB, SQLite), project-level `TypeMappings` override built-ins, `DataType` reference resolves `ClrType`/`MaxLength`/`DefaultValue`, `ClrType` direct override, unmapped types fall through to defaults, unknown `DataType` reference produces error. **SQLite-backed tests:** auto-population of `TypeMappings` from real introspected SQLite column types, newly discovered types get reasonable CLR defaults, existing mappings are not overwritten (add-only idempotent behavior) |

#### Override Resolution

| Test Class | Covers |
|------------|--------|
| `OverrideResolverTests` | Exact class match, `"*"` wildcard class match, exact class beats `"*"`, last-entry-wins for same specificity, property-level overrides with exact class, property-level overrides with wildcard class, class-level overrides (no `Property`), `Ignore: true` filters property for specific artifact, `Ignore: true` without artifact filters for all, `DataType` override applies custom type fields, `Variant` + `DataType` combined, same property with different variants per artifact |
| `VariantResolutionTests` | `GetVariant(artifactName)` returns correct variant per artifact, `GetVariant` returns empty string when no variant assigned, `GetVariant` for unmatched artifact returns empty string |

#### Relationship Merging

| Test Class | Covers |
|------------|--------|
| `RelationshipMergerTests` | Explicit relationships merged with introspected FKs, explicit overrides introspected when same dependent table + schema + columns, `ReferencingForeignKeys` populated as inverse of all FKs, `DependentSchema`/`PrincipalSchema` default to `Defaults.Schema`, cross-schema relationships, self-referencing relationships. **SQLite-backed tests:** merge explicit relationships with real FKs introspected from SQLite, explicit relationship overrides a real introspected FK, `ReferencingForeignKeys` includes both real and explicit sources |

#### Rendering

| Test Class | Covers |
|------------|--------|
| `DispatchTagTests` | Dispatches Model to `{Variant}Class` macro, dispatches Attribute to `{Variant}Property` macro, falls back to `DefaultClass` for Models with no variant, falls back to `DefaultProperty` for Attributes with no variant, writes error comment when macro not found, views dispatch identically to tables, `ArtifactName` read from context, dispatch works inside loops (SingleFile scenario) |
| `CustomFiltersTests` | `pascal_case`, `camel_case`, `snake_case`, `pluralize`, `singularize`, `type_nullable` with nullable/non-nullable, edge cases (empty string, already-cased input, single word) |
| `TemplateRenderingTests` | PerModel scope renders once per entity with `entity` in context, SingleFile scope renders once with `entities`/`views`/`schemas` in context, `ArtifactName` set to template key, `database` context contains `Database` and `Provider`, `config` context contains project configuration values, `parameters` pass-through from tool call, ignored properties filtered before rendering, complete entity template produces valid C# output |
| `FileWriterTests` | `Mode: "Always"` overwrites existing files, `Mode: "SkipExisting"` skips existing files, `Mode: "SkipExisting"` creates new files, output directory created if missing, UTF-8 without BOM encoding, OutputPattern resolved with `entity.Name` and `entity.Schema`, reports correct action per file (Created, Overwritten, SkippedExisting) |

#### DDL Generation

| Test Class | Covers |
|------------|--------|
| `SqlServerDdlGeneratorTests` | Full DDL output for SQL Server: `[bracket]` quoting, `IDENTITY(1,1)`, `bit` for boolean, CHECK constraint for enums, PK/FK/index generation, ON DELETE/ON UPDATE, notes as SQL comments, auto-generated constraint names, type mapping, empty schema, circular FK handling |
| `PostgreSqlDdlGeneratorTests` | Full DDL output for PostgreSQL: `"double-quote"` quoting, `GENERATED ALWAYS AS IDENTITY`, `CREATE TYPE AS ENUM`, `boolean`, PK/FK/index generation, ON DELETE/ON UPDATE, notes as SQL comments, auto-generated constraint names, type mapping |
| `MySqlDdlGeneratorTests` | Full DDL output for MySQL: `` `backtick` `` quoting, `AUTO_INCREMENT`, inline `ENUM(...)`, `tinyint(1)` for boolean, `ENGINE = INNODB`, PK/FK/index generation, ON DELETE/ON UPDATE, notes as SQL comments, auto-generated constraint names, type mapping |
| `SqliteDdlGeneratorTests` | Full DDL output for SQLite: `"double-quote"` quoting, `AUTOINCREMENT`, CHECK constraint for enums, `INTEGER` for boolean, type affinity, PK/FK/index generation, notes as SQL comments, auto-generated constraint names |
| `DdlTypeMapperTests` | All 28+ generic types mapped correctly for each dialect, unrecognized types passed through verbatim, precision/scale preserved on parameterized types, case-insensitive matching |

#### Diagrams

| Test Class | Covers |
|------------|--------|
| `DiagramGeneratorTests` | HTML output contains expected table boxes, column names/types/constraints, FK relationship lines, enum boxes, interactive JS (pan/zoom/drag handlers present), self-contained single file (no external dependencies), auto-layout positions FK-connected tables adjacent, empty schema produces valid HTML |

#### Caching

| Test Class | Covers |
|------------|--------|
| `TimestampCacheTests` | Cache hit when file unchanged, cache miss when file timestamp changes, config change invalidates all caches (config, templates, schema), template change invalidates only that template, fresh load on first access |

#### Logging

| Test Class | Covers |
|------------|--------|
| `LoggingServiceTests` | Enabled=true creates Serilog file logger, Enabled=false creates no-op logger (no file written), absent `Logging` section creates no-op logger, MEL `ILoggerFactory` routes through Serilog when enabled, log level filtering (e.g., `Information` suppresses `Debug`), connection string values are redacted in log output, `LogPath` creates parent directories if missing, rolling interval produces correctly named files, retained file count limit is applied |

#### End-to-End Pipeline (SQLite-backed)

| Test Class | Covers |
|------------|--------|
| `GeneratePipelineTests` | **Uses SQLite in-memory DB.** Full pipeline: create SQLite DB with tables/FKs/views → introspect schema → write `db-design.json` (introspected only) → apply type mappings → merge explicit relationships at generation time → apply overrides (variants, ignores, data types) → read `db-design.json` → render PerModel template → verify generated C# output contains correct class names, property types, navigation properties, and namespace. Also covers: SingleFile template (DbContext) renders with all entities, `SkipExisting` mode skips files that already exist, `Include`/`Exclude` filters reduce generated file set, views rendered when `IncludeViews=true`, views excluded when `IncludeViews=false`, dispatch tag resolves correct macros through the full pipeline, override `Ignore: true` removes properties from rendered output, explicit relationships added after introspection appear in generated output without re-introspecting |

### `Pondhawk.Persistence.Mcp.Tests` — Integration Tests

| Test Class | Covers |
|------------|--------|
| `InitToolTests` | Creates all expected files (including `.env`), returns error when `persistence.project.json` exists, `provider`, `namespace`, and `connectionString` parameters applied, `connectionString` written to `.env` when provided, provider-specific placeholder used when `connectionString` omitted, generated templates are valid Liquid, generated config includes `Logging` section disabled by default |
| `IntrospectSchemaToolTests` | **Uses SQLite in-memory DB.** Writes `db-design.json` with correct table/column/FK/index metadata (no explicit relationships from config), returns lightweight summary (not full schema), `include`/`exclude` parameters filter tables, `includeViews` parameter controls view inclusion, `schemas` parameter filters by schema, auto-populates `TypeMappings` in config file for newly discovered types, existing `TypeMappings` not overwritten |
| `GenerateToolTests` | Reads schema from `db-design.json`, merges explicit `Relationships` from config at generation time, returns error if `db-design.json` missing, `templates` parameter filters template execution, `models` parameter filters to exact names, `includeViews` parameter overrides config default, `schemas` parameter filters by schema, `parameters` pass-through to template context, partial failure reports per-file results |
| `ListTemplatesToolTests` | Returns all template entries with correct fields (Path, OutputPattern, Scope, Mode), handles empty `Templates` section, artifact names match template keys |
| `ValidateConfigToolTests` | Valid config returns no errors, missing template file reported, invalid `DataType` reference reported, output path collision warned, invalid `Logging.Level` value reported, invalid `Relationships` reference reported |
| `GenerateDdlToolTests` | Reads schema from `db-design.json`, generates dialect-specific DDL for each provider, requires `provider` parameter, defaults output path, returns error if `db-design.json` missing, invalid provider returns error listing valid values, merges Relationships from config when present, works without `persistence.project.json`, validates db-design.json against JSON Schema before generation |
| `GenerateDiagramToolTests` | Reads schema from `db-design.json`, generates HTML diagram, defaults output path, returns error if `db-design.json` missing, merges Relationships from config when present, works without `persistence.project.json`, validates db-design.json against JSON Schema before generation |
| `UpdateToolTests` | Refreshes AGENTS.md and db-design.json to latest content after init, normalizes config (AppliesTo survives round-trip), throws error when no config exists (points to init), preserves existing config values (TypeMappings, Overrides) through update |

### Test Fixtures

The `Fixtures/` directory contains reusable test data and helpers:

- **`SqliteTestDatabase.cs`** — Helper class that creates SQLite in-memory databases with configurable schemas for testing. Provides builder-style API to define tables, columns, primary keys, foreign keys, indexes, and views via DDL execution. The connection stays open for the lifetime of the test (SQLite in-memory databases are destroyed when the connection closes). Implements `IDisposable` for clean teardown. Example usage:

  ```csharp
  using var db = new SqliteTestDatabase()
      .AddTable("Categories", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL")
      .AddTable("Products", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, CategoryId INTEGER, FOREIGN KEY (CategoryId) REFERENCES Categories(Id)")
      .AddView("ActiveProducts", "SELECT * FROM Products WHERE Name IS NOT NULL")
      .Build();
  // db.ConnectionString → use for introspection
  ```

- **`SampleConfigs/`** — `persistence.project.json` files for various scenarios (minimal, full-featured, multi-schema, invalid, missing fields)
- **`SampleTemplates/`** — `.liquid` templates for rendering tests (entity with variants, stub, DbContext, templates with syntax errors)

---

## 13. Error Handling

The MCP server is a long-lived process. Errors in individual tool calls must never crash the server. All failures are returned as structured MCP error responses with actionable messages.

### Error Response Format

Tool errors use the MCP `isError` flag with a human-readable message:

```json
{
  "isError": true,
  "content": [
    {
      "type": "text",
      "text": "Connection failed: Unable to connect to SQL Server at localhost:1433. Verify the server is running and the connection string is correct.\n\nProvider: sqlserver\nError: A network-related or instance-specific error occurred..."
    }
  ]
}
```

### Failure Categories

#### Startup Failures

These occur before any tool calls are processed.

| Scenario | Behavior |
|----------|----------|
| `--project` arg missing | Server exits with non-zero exit code and stderr message |
| Project path doesn't exist | Server exits with non-zero exit code and stderr message |
| `persistence.project.json` not found | Server starts normally — tools return a config error, allowing the AI to call `init` to create the file |

#### Configuration Errors

| Scenario | Behavior |
|----------|----------|
| Malformed JSON | Error with parse location (line/column) |
| Missing required field | Error naming the field and section |
| Invalid `DataType` reference in override | Error naming the override and the missing type |
| Invalid `Relationships` entry | Error naming the entry and the non-existent table or column |
| Template file not found | Error with the expected file path |

#### Environment Variable Resolution

| Scenario | Behavior |
|----------|----------|
| `${VAR}` not set in env or `.env` | Error naming the variable |
| `.env` file malformed | Warning (continue with system env vars only) |

#### Database Connection Failures

| Scenario | Behavior |
|----------|----------|
| Connection refused / timeout | Error with provider, host (no credentials exposed), and underlying exception message |
| Authentication failed | Error indicating auth failure (no credentials in message) |
| Missing ADO.NET provider | Error naming the required provider NuGet package |

#### Schema Introspection Failures

| Scenario | Behavior |
|----------|----------|
| Permission denied on schema | Error naming the schema and suggesting required permissions |
| No tables match include/exclude filters | Warning — return empty result with explanatory message, not an error |
| `db-design.json` exists with `"Origin": "design"` | Error: `"db-design.json has Origin 'design' and cannot be overwritten by introspection. This is a design-first schema. To switch to database-first, delete db-design.json or change Origin to 'introspected'."` |

#### Schema File Errors

| Scenario | Behavior |
|----------|----------|
| `db-design.json` not found when `generate` is called | Error instructing the AI agent to run `introspect_schema` first |
| `db-design.json` contains malformed JSON | Error with parse location (line/column) |

#### Template Rendering Failures

Fluid uses a `TryParse` pattern for parsing (returns `false` with error messages, no exception). For rendering, Fluid is lenient by default — undefined variables render as empty strings and unknown filters pass through silently. To catch template errors rather than producing silently wrong output, the server enables strict mode:

- **`StrictVariables = true`** — throws `FluidException` when a template references an undefined variable
- **`StrictFilters = true`** — throws `FluidException` when a template uses an unregistered filter

| Scenario | Behavior |
|----------|----------|
| Liquid syntax error (parse failure) | Error with template path, line number, and parser message (from `TryParse` error output) |
| Undefined variable in template | Error with template path, variable name, and entity being rendered (thrown by strict mode) |
| Unknown filter in template | Error with template path and filter name (thrown by strict mode) |
| Dispatch macro not found | Inline error comment in generated output (`/* dispatch error: macro 'X' not found */`) — not a tool-level failure |
| Other rendering exception | Error with template path, entity name being rendered, and exception message |

#### File System Failures

| Scenario | Behavior |
|----------|----------|
| Output directory doesn't exist | Create it automatically (recursive mkdir) |
| Permission denied writing file | Error with the file path |
| Partial failure (some files succeed, some fail) | **Continue** — write all files that succeed, report failures per-file in the response |

### Design Principles

- **Never crash the server** — all tool call failures return MCP error responses; the process stays alive for subsequent calls
- **Partial success on generate** — if 8 of 10 files write successfully and 2 fail, report the 8 successes and 2 failures individually; do not roll back successful writes
- **No credentials in error messages** — connection errors reference the provider, not the connection string; passwords, tokens, and connection strings are never included in error output
- **Actionable messages** — every error tells the AI agent or developer what to do next
- **`validate_config` catches most errors early** — the AI agent should call this proactively to surface configuration issues before attempting generation

---

## 14. Build System

The project uses **Cake Frosting** for build automation. Cake Frosting is a .NET console application that defines build tasks as C# classes, providing a type-safe, IDE-friendly build pipeline with no external tooling beyond the .NET SDK.

### Build Project

The `build/` directory contains a standalone .NET 10 console application (`Build.csproj`) that references the `Cake.Frosting` NuGet package. It is **not** included in the main solution's build output — it is a development tool only. Build tasks reference the solution file at the repository root (`../pondhawk-mcp.slnx` relative to the build project).

Run the build from the repository root:

```bash
dotnet run --project build -- --target <TaskName>
```

### Tasks

| Task | Depends On | Description |
|------|-----------|-------------|
| `Clean` | — | Deletes `bin/`, `obj/`, and `publish/` directories across all projects |
| `Restore` | `Clean` | Runs `dotnet restore` on the solution |
| `Build` | `Restore` | Runs `dotnet build` on the solution in `Release` configuration |
| `Test` | `Build` | Runs `dotnet test` on both `Pondhawk.Persistence.Core.Tests` and `Pondhawk.Persistence.Mcp.Tests` |
| `Publish` | `Test` | Publishes `Pondhawk.Persistence.Mcp` as self-contained, single-file executables for all target platforms |

The **default task** is `Test` — running the build with no target argument cleans, restores, builds, and tests.

### Publish Targets

The `Publish` task produces self-contained, single-file executables for each target platform. All four RIDs are built in a single invocation.

| Platform       | Runtime Identifier | Output Binary            |
|----------------|--------------------|--------------------------|
| Windows x64    | `win-x64`          | `pondhawk-persistence-mcp.exe`        |
| macOS ARM64    | `osx-arm64`        | `pondhawk-persistence-mcp`            |
| Linux x64      | `linux-x64`        | `pondhawk-persistence-mcp`            |
| Linux ARM64    | `linux-arm64`      | `pondhawk-persistence-mcp`            |

### Publish Configuration

All publish targets use the following `dotnet publish` settings:

| Setting | Value | Rationale |
|---------|-------|-----------|
| `--configuration` | `Release` | Optimized build |
| `--self-contained` | `true` | No .NET runtime required on target machine |
| `-p:PublishSingleFile=true` | — | Single executable, no loose DLLs |
| `-p:PublishTrimmed=false` | — | Trimming disabled — Fluid uses reflection internally which causes runtime failures when trimmed |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | — | Pack native libraries (e.g., SQLite) into the single file |
| `-p:EnableCompressionInSingleFile=true` | — | Compress the single file to further reduce size |
| `--output` | `publish/{rid}/` | Per-RID output directory relative to the repository root |

Output structure:

```
publish/
├── win-x64/
│   └── pondhawk-persistence-mcp.exe
├── osx-arm64/
│   └── pondhawk-persistence-mcp
├── linux-x64/
│   └── pondhawk-persistence-mcp
└── linux-arm64/
    └── pondhawk-persistence-mcp
```

### Trimming Considerations

Trimming (`PublishTrimmed=true`) was evaluated but is **intentionally disabled**. The Fluid template engine relies on reflection internally for template parsing and rendering, which causes runtime failures when IL trimming removes reflected-upon types. The trade-off is a larger binary (~30-50 MB self-contained) but guaranteed runtime stability.

If future Fluid versions add trim annotations or a source-generator mode, trimming can be re-evaluated. Other dependencies (System.Text.Json with source generators, Serilog) are trim-compatible.

---

## 15. Future Considerations

The following are explicitly out of scope for the initial version but may be considered in future iterations:

- **Multiple connections per project**: Supporting multiple database connections in a single project (currently one connection per project; use separate MCP server instances for multiple databases)
- **MCP Resources**: Exposing schemas and templates as readable MCP resources
- **MCP Prompts**: Guided prompt templates for common generation workflows
- **Incremental generation**: Detecting schema changes and regenerating only affected files
- **Template marketplace**: Sharing and discovering community templates
- **Additional providers**: Oracle, CockroachDB (partial PostgreSQL wire-compatibility exists via Npgsql but is not officially supported)
- **Schema diffing**: Comparing schema versions and generating migration code
- **Many-to-many skip navigations**: Detecting junction tables and generating direct skip navigation properties (e.g., `Product.Tags` bypassing `ProductTag`). Many-to-many via explicit junction entities already works with the current `ForeignKeys[]` / `ReferencingForeignKeys[]` model
- **Multi-schema DDL**: Generating DDL that spans multiple database schemas with `CREATE SCHEMA` statements
- **Migration diffing**: Comparing two `db-design.json` versions and generating `ALTER TABLE` migration scripts
- **`COMMENT ON` support**: Using PostgreSQL and MySQL native `COMMENT ON` statements for table/column notes instead of SQL comments
- **DBML import/export**: Importing from or exporting to DBML format for interoperability with dbdiagram.io and other tools

---

## 16. Design-First Schema, DDL Generation, and Schema Diagrams

### 16.1 Feature Overview

pondhawk-mcp currently supports a **database-first** workflow: introspect an existing database into `db-design.json`, then generate C# code via Liquid templates. This section adds a complementary **design-first** pipeline where an AI agent designs a new database schema by writing `db-design.json` directly, and the MCP server generates:

1. **Dialect-specific DDL SQL** — for deploying the schema to a target database
2. **Interactive HTML ER diagram** — for visual review of the schema

Both new capabilities work with **any** `db-design.json` — whether hand-designed by the AI agent or introspected from a database. They complement (do not replace) the existing database-first introspection pipeline.

**Key architectural decision:** Rather than introducing DBML as a new intermediate format (with a parser and new AST), `db-design.json` is extended to serve both directions. The AI agent writes JSON natively and accurately. The `db-design.json` format already captures tables, columns, types, constraints, FKs, and indexes — nearly everything needed for DDL generation. Optional fields are added for enums and notes, and introspection-specific fields are made optional for design-first use. No new dependencies are required.

**Pipeline summary:**

```
Design-first:     Claude writes db-design.json → generate_ddl   → DDL SQL file → deploy to database
                   Claude writes db-design.json → generate_diagram → interactive HTML ER diagram

Database-first:   introspect_schema → db-design.json → generate → C# code (unchanged)
```

---

### 16.2 Schema.json Extensions

All changes to the `db-design.json` format are **additive and non-breaking**. Existing `db-design.json` files produced by `introspect_schema` remain valid without modification. The new fields are only populated for design-first schemas.

#### `Origin` Field (Required)

The `Origin` field is a top-level string that identifies how the `db-design.json` file was created and controls overwrite protection:

```json
{
  "$schema": "db-design.schema.json",
  "Origin": "design"
}
```

| Value | Set By | Meaning |
|-------|--------|---------|
| `"introspected"` | `introspect_schema` | File was generated from a live database. `introspect_schema` can freely overwrite it. |
| `"design"` | AI agent (Claude) | File was hand-authored for design-first use. `introspect_schema` **refuses to overwrite** and returns an error. |

**Behavior:**

- `introspect_schema` always writes `"Origin": "introspected"` when creating or overwriting `db-design.json`
- Before writing, `introspect_schema` checks: if `db-design.json` exists and has `"Origin": "design"`, it returns an error and does not overwrite
- If `db-design.json` exists with `"Origin": "introspected"` or no `Origin` field (legacy files), `introspect_schema` overwrites normally
- `generate`, `generate_ddl`, and `generate_diagram` ignore the `Origin` field — they work identically with both values
- To switch a design-first schema back to database-first, the user must manually delete `db-design.json` or change `Origin` to `"introspected"`

#### New Optional Fields

**`Enums[]`** — top-level array of enum type definitions:

```json
{
  "Enums": [
    {
      "Name": "OrderStatus",
      "Note": "Tracks the lifecycle of an order",
      "Values": [
        { "Name": "Pending", "Note": "Order placed but not confirmed" },
        { "Name": "Confirmed" },
        { "Name": "Shipped" },
        { "Name": "Delivered" },
        { "Name": "Cancelled" }
      ]
    }
  ]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Enums[].Name` | `string` | Yes | Enum type name |
| `Enums[].Note` | `string` | No | Descriptive note, emitted as SQL comment |
| `Enums[].Values[]` | `array` | Yes | Ordered list of enum values |
| `Enums[].Values[].Name` | `string` | Yes | Enum value name |
| `Enums[].Values[].Note` | `string` | No | Descriptive note for this value |

**`Note`** — optional string on tables and columns:

```json
{
  "Tables": [
    {
      "Name": "Orders",
      "Note": "Customer purchase orders",
      "Columns": [
        {
          "Name": "Status",
          "DataType": "varchar(20)",
          "Note": "References OrderStatus enum"
        }
      ]
    }
  ]
}
```

Notes are emitted as SQL comments in the DDL output (e.g., `-- Customer purchase orders` before the CREATE TABLE statement).

**`OnUpdate`** — optional string on ForeignKeys, alongside existing `OnDelete`:

```json
{
  "ForeignKeys": [
    {
      "Columns": ["CategoryId"],
      "PrincipalTable": "Categories",
      "PrincipalColumns": ["Id"],
      "OnDelete": "Cascade",
      "OnUpdate": "Cascade"
    }
  ]
}
```

Valid values for both `OnDelete` and `OnUpdate`: `Cascade`, `SetNull`, `SetDefault`, `NoAction`, `Restrict`.

#### Fields Made Optional for Design-First

The following fields are required for introspected schemas but optional for design-first schemas:

| Field | Why Optional | Generator Behavior |
|-------|-------------|-------------------|
| `ClrType` on columns | Meaningless for DDL generation — no .NET types in SQL | Ignored by DDL generator; code generation still requires it |
| `ReferencingForeignKeys[]` on tables | Computed inverse — redundant with `ForeignKeys[]` | Generator computes from `ForeignKeys[]` if absent |
| `Provider` at top level | Generator takes provider as a parameter | Not read by `generate_ddl` |
| `Database` at top level | Not needed for DDL generation | Optional |
| Constraint/index `Name` | Names can be auto-generated | If null, DDL generator creates names: `PK_{table}`, `FK_{table}_{ref}`, `IX_{table}_{cols}` |

**No changes to existing introspection behavior** — `introspect_schema` continues to write the same format it does today, with the addition of `"Origin": "introspected"`. The new fields (`Enums`, `Note`, `OnUpdate`) are never populated by introspection.

---

### 16.2.1 JSON Schema for db-design.json

A formal JSON Schema file (`db-design.schema.json`) provides validation and IDE autocompletion for `db-design.json`. This follows the existing pattern where `persistence.project.schema.json` validates the project config.

**Lifecycle:**

- Generated by `init` alongside `persistence.project.schema.json`
- Refreshed by `update` to pick up new schema extensions
- `db-design.json` includes a `$schema` reference for IDE support: `"$schema": "db-design.schema.json"`
- `introspect_schema` writes the `$schema` field when generating `db-design.json`

**Validation performed by tools:**

| Tool | Validation |
|------|-----------|
| `generate_ddl` | Validates `db-design.json` against JSON Schema before generating DDL; returns structured errors on failure |
| `generate_diagram` | Validates `db-design.json` against JSON Schema before generating diagram; returns structured errors on failure |
| `validate_config` | Extended to also validate `db-design.json` if it exists (optional, since `db-design.json` may not exist yet) |

**JSON Schema design:**

- `required` only for fields that must always be present: `Origin`, table `Name`, column `Name`, column `DataType`
- `Origin` constrained to enum: `introspected`, `design`
- Introspection-specific fields (`ClrType`, `ReferencingForeignKeys`) are optional
- `OnDelete`/`OnUpdate` constrained to enum: `Cascade`, `SetNull`, `SetDefault`, `NoAction`, `Restrict`
- `IsNullable`/`IsPrimaryKey`/`IsIdentity` validated as booleans
- Permissive enough for both introspected and design-first schemas, strict enough to catch real errors (missing table names, invalid enum references, malformed FK definitions)

**Validation error format:**

```json
{
  "isError": true,
  "content": [
    {
      "type": "text",
      "text": "db-design.json validation failed:\n- $.Schemas[0].Tables[0].Columns[2]: Missing required field 'DataType'\n- $.Schemas[0].Tables[1].ForeignKeys[0].OnDelete: Invalid value 'Delete'. Must be one of: Cascade, SetNull, SetDefault, NoAction, Restrict"
    }
  ]
}
```

---

### 16.3 DDL Generator Specification

#### Architecture

The DDL generator uses an interface `IDdlGenerator` with 4 implementations (SqlServer, PostgreSql, MySql, Sqlite), selected by a factory method based on the `provider` parameter. All implementations live in `Pondhawk.Persistence.Core/Ddl/`.

```
IDdlGenerator
├── SqlServerDdlGenerator
├── PostgreSqlDdlGenerator
├── MySqlDdlGenerator
└── SqliteDdlGenerator
```

`DdlGeneratorFactory.Create(provider)` returns the appropriate implementation. Invalid providers return an error listing valid values.

#### Output Ordering

DDL statements are ordered to avoid forward references:

1. **Enum types** — PostgreSQL: `CREATE TYPE AS ENUM`; other dialects handle inline or via CHECK constraints
2. **Tables** — in dependency order (topological sort on FK references)
3. **Indexes** — `CREATE INDEX` statements
4. **Foreign keys** — `ALTER TABLE ADD CONSTRAINT FOREIGN KEY` statements (separate from CREATE TABLE to handle circular references)

#### Per-Dialect Differences

| Feature | SQL Server | PostgreSQL | MySQL | SQLite |
|---------|-----------|-----------|-------|--------|
| Identifier quoting | `[name]` | `"name"` | `` `name` `` | `"name"` |
| Auto-increment | `IDENTITY(1,1)` | `GENERATED ALWAYS AS IDENTITY` | `AUTO_INCREMENT` | `AUTOINCREMENT` |
| Enum handling | CHECK constraint | `CREATE TYPE AS ENUM` | Inline `ENUM(...)` | CHECK constraint |
| Boolean type | `bit` | `boolean` | `tinyint(1)` | `INTEGER` |
| Table suffix | — | — | `ENGINE = INNODB` | — |
| Notes | SQL comments (`--`) | SQL comments (`--`) | SQL comments (`--`) | SQL comments (`--`) |

#### Type Mapping

`DdlTypeMapper` maps ~28 generic/common data types to dialect-specific SQL types. The table below shows representative mappings:

| Generic Type | SQL Server | PostgreSQL | MySQL | SQLite |
|-------------|-----------|-----------|-------|--------|
| `int` | `int` | `integer` | `int` | `INTEGER` |
| `bigint` | `bigint` | `bigint` | `bigint` | `INTEGER` |
| `smallint` | `smallint` | `smallint` | `smallint` | `INTEGER` |
| `tinyint` | `tinyint` | `smallint` | `tinyint` | `INTEGER` |
| `boolean` / `bool` | `bit` | `boolean` | `tinyint(1)` | `INTEGER` |
| `decimal` / `numeric` | `decimal` | `numeric` | `decimal` | `REAL` |
| `float` | `float` | `double precision` | `double` | `REAL` |
| `real` | `real` | `real` | `float` | `REAL` |
| `money` | `money` | `money` | `decimal(19,4)` | `REAL` |
| `varchar` | `varchar` | `varchar` | `varchar` | `TEXT` |
| `nvarchar` | `nvarchar` | `varchar` | `varchar` | `TEXT` |
| `char` | `char` | `char` | `char` | `TEXT` |
| `nchar` | `nchar` | `char` | `char` | `TEXT` |
| `text` | `text` | `text` | `text` | `TEXT` |
| `ntext` | `ntext` | `text` | `text` | `TEXT` |
| `datetime` | `datetime` | `timestamp` | `datetime` | `TEXT` |
| `datetime2` | `datetime2` | `timestamp` | `datetime(6)` | `TEXT` |
| `date` | `date` | `date` | `date` | `TEXT` |
| `time` | `time` | `time` | `time` | `TEXT` |
| `timestamp` | `rowversion` | `timestamp` | `timestamp` | `TEXT` |
| `uuid` / `uniqueidentifier` | `uniqueidentifier` | `uuid` | `char(36)` | `TEXT` |
| `binary` | `binary` | `bytea` | `binary` | `BLOB` |
| `varbinary` | `varbinary` | `bytea` | `varbinary` | `BLOB` |
| `blob` | `varbinary(max)` | `bytea` | `blob` | `BLOB` |
| `image` | `image` | `bytea` | `longblob` | `BLOB` |
| `xml` | `xml` | `xml` | `text` | `TEXT` |
| `json` | `nvarchar(max)` | `jsonb` | `json` | `TEXT` |
| `jsonb` | `nvarchar(max)` | `jsonb` | `json` | `TEXT` |

**Unrecognized types** are passed through verbatim (e.g., a custom PostgreSQL domain type). **Precision/scale** on parameterized types (e.g., `decimal(18,2)`) is preserved.

#### Relationship Merging

If `persistence.project.json` exists and has explicit `Relationships`, they are merged with `db-design.json` ForeignKeys using the same merge logic as the `generate` tool (see section 5, Relationships). If no config file exists, only `db-design.json` ForeignKeys are used.

#### Circular FK Handling

When a topological sort detects circular FK references (e.g., table A references table B and table B references table A), the generator:

1. Breaks the cycle by deferring one FK in the cycle
2. Creates both tables without the deferred FK
3. Adds the deferred FK as an `ALTER TABLE ADD CONSTRAINT` statement after both tables exist

This is the same technique used for all FKs (output as ALTER TABLE after CREATE TABLE), so circular references are handled naturally.

---

### 16.4 HTML ER Diagram Generator

#### Output Format

A single self-contained `.html` file with all CSS, JavaScript, and SVG embedded inline. No external dependencies — the file can be opened directly in any modern browser without a web server.

#### Visual Elements

- **Tables** — styled boxes with a colored header containing the table name, followed by rows for each column showing:
  - Column name
  - Data type
  - Constraint icons: PK (key icon), FK (arrow icon), unique (U), not-null (!)
- **FK relationships** — SVG lines/arrows connecting FK columns to their referenced PK columns
- **Enum types** — distinct boxes with the enum name and values listed
- **Title bar** — fixed bar at the top showing project name (from `ProjectName` config or "ER Diagram"), description (if set), and generation date
- **Sidebar** — left panel listing entity groups for filtering; header reads "Entities"; includes a search box for finding tables by name
- **Zoom toolbar** — floating bottom-right control bar with zoom in (+), zoom out (−), zoom level indicator, and zoom-to-fit button
- **Color coding:**
  - PK columns highlighted (e.g., gold background)
  - FK columns highlighted (e.g., blue text)
  - Nullable columns visually distinct from not-null (e.g., italic or lighter text)

#### Interactive Features (Embedded JavaScript)

| Feature | Implementation |
|---------|---------------|
| Pan | Click-drag on background translates the viewport |
| Zoom | Scroll wheel scales the viewport around the cursor |
| Drag tables | Click-drag on a table header repositions that table; relationship lines update dynamically |
| Hover highlight | Hovering on an FK line highlights both the source and target columns/tables |
| Title bar | Shows project name, description, and generation date; always visible |
| Zoom toolbar | Floating bottom-right bar with +/− buttons, zoom level label, and Fit button |
| Search | Sidebar search box filters entities by name; clicking a result zooms in and centers the entity with a gold highlight |
| Click-to-navigate | In All view, clicking an entity in a FK-chain group navigates to that group's detail view |
| View state memory | All view remembers zoom level and pan position when switching to a group and back |
| All view layout | Group-sorted grid layout with no relationship lines; entities ordered by group for visual clustering |

All interactivity is implemented in vanilla JavaScript with SVG rendering. No external JS libraries (no D3, no mermaid, no vis.js).

#### Auto-Layout

The diagram generator positions tables automatically using:

1. **Topological sort** on FK dependencies — tables with no FKs are placed first
2. **Grid placement** — tables arranged in rows/columns with consistent spacing
3. **FK adjacency** — FK-connected tables are placed adjacent to each other when possible

The auto-layout produces a reasonable initial arrangement. Users can drag tables to customize positioning.

#### Relationship Merging

Same behavior as `generate_ddl` — if `persistence.project.json` exists and has explicit `Relationships`, they are merged with `db-design.json` ForeignKeys.

---

### 16.5 New MCP Tools

#### `generate_ddl`

Generates dialect-specific DDL SQL from `db-design.json` and writes it to a file.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `provider` | `string` | Yes | Target database dialect: `sqlserver`, `postgresql`, `mysql`, `sqlite` |
| `output` | `string` | No | Output file path, relative to project root. Default: `db-design.{provider}.sql` |

**Behavior:**

1. Reads `db-design.json` from the project directory
2. Validates `db-design.json` against the JSON Schema (`db-design.schema.json`); returns structured errors on failure
3. Optionally reads `persistence.project.json` and merges explicit `Relationships` into FK data (if the config file exists)
4. Selects the dialect-specific DDL generator based on `provider`
5. Generates DDL SQL (enum types → tables in dependency order → indexes → foreign keys)
6. Writes the DDL to the output file (always overwrites, UTF-8 without BOM)
7. Returns a summary

**Returns:**

```json
{
  "Provider": "postgresql",
  "OutputFile": "db-design.postgresql.sql",
  "Summary": {
    "EnumTypes": 2,
    "Tables": 8,
    "Indexes": 5,
    "ForeignKeys": 12
  }
}
```

**Errors:**

| Scenario | Error Message |
|----------|--------------|
| `db-design.json` not found | `"db-design.json not found in project directory. Create it manually for design-first use, or run introspect_schema to generate it from an existing database."` |
| `db-design.json` fails JSON Schema validation | Structured errors with field paths and violation descriptions |
| Invalid `db-design.json` JSON | Parse error with location |
| Invalid `provider` | `"Invalid provider 'X'. Valid values: sqlserver, postgresql, mysql, sqlite"` |
| File write error | Error with file path |
| Empty schema (no tables) | Warning — generates empty DDL with a header comment |

---

#### `generate_diagram`

Generates an interactive HTML ER diagram from `db-design.json` and writes it to a file.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `output` | `string` | No | Output file path, relative to project root. Default: `db-design.html` |

**Behavior:**

1. Reads `db-design.json` from the project directory
2. Validates `db-design.json` against the JSON Schema (`db-design.schema.json`); returns structured errors on failure
3. Optionally reads `persistence.project.json` and merges explicit `Relationships` into FK data (if the config file exists)
4. Generates the HTML ER diagram (tables, columns, FK relationships, enums, interactive JS)
5. Writes the HTML to the output file (always overwrites)
6. Returns a summary

**Returns:**

```json
{
  "OutputFile": "db-design.html",
  "Summary": {
    "Tables": 8,
    "Relationships": 12,
    "EnumTypes": 2
  }
}
```

**Errors:**

| Scenario | Error Message |
|----------|--------------|
| `db-design.json` not found | Same as `generate_ddl` |
| `db-design.json` fails JSON Schema validation | Structured errors with field paths and violation descriptions |
| Invalid `db-design.json` JSON | Parse error with location |
| File write error | Error with file path |

---

#### Common Behavior (Both Tools)

- Work without `persistence.project.json` existing — they only need `db-design.json`
- Work with any `db-design.json` — whether hand-designed by the AI agent or introspected from a database
- Schema is read fresh each time (no caching — these are typically one-shot operations)
- Return errors if `db-design.json` doesn't exist
- Merge explicit `Relationships` from config when the config file exists

---

### 16.6 Solution Structure Additions

The new files added to the existing solution structure (see section 3 for the complete structure):

```
src/Pondhawk.Persistence.Core/
├── Configuration/
│   └── DbDesignFileSchema.cs                 ← new (embedded JSON Schema for db-design.json,
│                                              mirrors ProjectConfigurationSchema.cs pattern)
├── Ddl/                                    ← new directory
│   ├── IDdlGenerator.cs                    (interface: GenerateDdl(schema) → string)
│   ├── DdlGeneratorFactory.cs              (factory: Create(provider) → IDdlGenerator)
│   ├── SqlServerDdlGenerator.cs            (SQL Server dialect)
│   ├── PostgreSqlDdlGenerator.cs           (PostgreSQL dialect)
│   ├── MySqlDdlGenerator.cs                (MySQL dialect)
│   ├── SqliteDdlGenerator.cs               (SQLite dialect)
│   └── DdlTypeMapper.cs                    (generic-to-dialect type mappings)
├── Diagrams/                               ← new directory
│   └── DiagramGenerator.cs                 (HTML ER diagram generator)

src/Pondhawk.Persistence.Mcp/
├── Tools/
│   ├── GenerateDdlTool.cs                  ← new
│   └── GenerateDiagramTool.cs              ← new

tests/Pondhawk.Persistence.Core.Tests/
├── Ddl/                                    ← new directory
│   ├── SqlServerDdlGeneratorTests.cs
│   ├── PostgreSqlDdlGeneratorTests.cs
│   ├── MySqlDdlGeneratorTests.cs
│   ├── SqliteDdlGeneratorTests.cs
│   └── DdlTypeMapperTests.cs
├── Diagrams/                               ← new directory
│   └── DiagramGeneratorTests.cs

tests/Pondhawk.Persistence.Mcp.Tests/
├── Tools/
│   ├── GenerateDdlToolTests.cs             ← new
│   └── GenerateDiagramToolTests.cs         ← new
```

---

### 16.7 Acceptance Criteria

- **JSON Schema validation:** `db-design.schema.json` generated by `init` and refreshed by `update`; `db-design.json` includes `$schema` reference; `generate_ddl` and `generate_diagram` validate against JSON Schema before processing; validation errors return structured messages with field paths; schema is permissive for both introspected and design-first schemas
- **DDL generators:** CREATE TABLE, PK, NOT NULL, UNIQUE, DEFAULT, auto-increment, indexes, FKs with ON DELETE/ON UPDATE, enum handling per dialect, type mapping, auto-generated constraint names, circular FK handling, notes as SQL comments
- **Dialect-specific:** SQL Server (`IDENTITY`, CHECK for enums, `[bracket]` quoting), PostgreSQL (`CREATE TYPE AS ENUM`, `GENERATED ALWAYS AS IDENTITY`), MySQL (`AUTO_INCREMENT`, inline `ENUM`, `ENGINE = INNODB`), SQLite (`AUTOINCREMENT`, CHECK, type affinity)
- **HTML diagram:** tables rendered with columns/types/constraints, FK relationships as lines, interactive pan/zoom/drag, self-contained single file, auto-layout
- **MCP tools:** `generate_ddl` requires provider, defaults work, errors on missing `db-design.json`, works without config, merges Relationships when config exists
- **Origin-based overwrite protection:** `introspect_schema` writes `"Origin": "introspected"`; refuses to overwrite when `Origin` is `"design"`; missing `Origin` treated as `"introspected"` for backward compatibility
- **Schema.json extensions:** enums, notes, OnUpdate — all optional, backward compatible

---

### 16.8 Testing Strategy

#### Per-Dialect DDL Generator Tests

Each dialect has a dedicated test class that verifies the full DDL output for a representative schema:

- Tables with various column types, PK, NOT NULL, UNIQUE, DEFAULT, auto-increment
- Indexes (unique and non-unique)
- Foreign keys with ON DELETE and ON UPDATE
- Enum types (dialect-specific handling)
- Notes emitted as SQL comments
- Auto-generated constraint names when Name is null
- Empty schema produces valid but empty DDL
- Circular FK references handled correctly
- Tables output in dependency order

#### Type Mapping Tests

`DdlTypeMapperTests` covers:

- All 28+ generic types mapped correctly for each of the 4 dialects
- Unrecognized types passed through verbatim
- Precision/scale preserved on parameterized types (e.g., `decimal(18,2)`)
- Case-insensitive type matching

#### Diagram Generator Tests

`DiagramGeneratorTests` covers:

- HTML output contains expected table boxes with correct column names, types, and constraint indicators
- FK relationships rendered as lines between tables
- Enum types rendered as distinct boxes
- Interactive JS handlers present (pan, zoom, drag)
- Self-contained single file (no external resource references)
- Auto-layout positions FK-connected tables adjacent
- Empty schema produces valid HTML with no table boxes
- Title bar renders with project name, description, and date
- Title bar defaults to "ER Diagram" without project name
- Sidebar header text is "Entities"
- Zoom toolbar with +/−, level display, and Fit button
- Search box filters and highlights entities
- Click-to-navigate from All view to group detail
- All view uses group-sorted grid without relationship lines
- All view state (zoom/pan) preserved across group switches

#### MCP Tool Integration Tests

`GenerateDdlToolTests` and `GenerateDiagramToolTests` cover:

- Reads schema from `db-design.json` and generates correct output
- `generate_ddl` requires `provider` parameter; invalid provider returns error with valid values
- Default output paths work (`db-design.{provider}.sql`, `db-design.html`)
- Returns error if `db-design.json` doesn't exist
- Merges Relationships from config when `persistence.project.json` exists
- Works without `persistence.project.json`
- Validates `db-design.json` against JSON Schema before generation

---

### 16.9 Error Handling

| Scenario | Tool(s) | Behavior |
|----------|---------|----------|
| `db-design.json` not found | Both | Error with instructions: create manually or run `introspect_schema` |
| `db-design.json` fails JSON Schema validation | Both | Error with field path and violation description |
| Invalid `db-design.json` JSON | Both | Error with parse location (line/column) |
| Invalid `provider` | `generate_ddl` | Error listing valid values: `sqlserver`, `postgresql`, `mysql`, `sqlite` |
| File write error | Both | Error with the output file path |
| Empty schema (no tables) | `generate_ddl` | Warning — generates empty DDL with a header comment |
| Empty schema (no tables) | `generate_diagram` | Generates valid HTML with no table boxes |

---

### 16.10 AGENTS.md Updates

The `AGENTS.md` file (generated by `init`, refreshed by `update`) is extended with a new section covering the design-first workflow:

- **Design-first pipeline** — how to author `db-design.json` by hand, covering required vs optional fields, enum definitions, notes, and OnUpdate
- **`Origin` field** — always set `"Origin": "design"` when hand-authoring `db-design.json`; this prevents `introspect_schema` from accidentally overwriting the file. To switch back to database-first, delete `db-design.json` or change `Origin` to `"introspected"`.
- **`generate_ddl` usage** — parameters, output format, provider selection, when to use
- **`generate_diagram` usage** — parameters, output format, how to open the HTML file
- **Workflow examples:**
  - Design a new database: write `db-design.json` (with `"Origin": "design"`) → `generate_ddl` → review SQL → deploy
  - Visualize an existing database: `introspect_schema` → `generate_diagram` → open HTML
  - Design and visualize: write `db-design.json` (with `"Origin": "design"`) → `generate_diagram` → review → `generate_ddl` → deploy
- **Schema.json field reference** — quick reference for all fields, marking which are required vs optional for design-first use
- **JSON Schema validation** — `db-design.schema.json` provides IDE autocompletion; both tools validate before processing
