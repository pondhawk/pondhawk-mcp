# pondhawk-mcp

An MCP (Model Context Protocol) server that enables AI agents to introspect relational database schemas and generate customizable code artifacts using Liquid templates. Built with C# on .NET 10.

pondhawk-mcp bridges the gap between existing database schemas and modern .NET code by letting developers define their own generation templates as project assets, committed alongside application code. Templates are primarily authored and maintained by AI agents (e.g., Claude) as part of the development workflow.

## What It Does

- **Schema Introspection** — Connects to SQL Server, PostgreSQL, MySQL/MariaDB, or SQLite databases and reads table/view/column/FK/index metadata into a portable `db-design.json` file
- **Template-Driven Code Generation** — Renders Liquid templates against schema data to produce EF Core entities, DTOs, DbContext classes, or any other code artifact you define
- **Design-First DDL Generation** — Generates dialect-specific SQL DDL from a hand-authored `db-design.json` for deploying new databases
- **ER Diagram Generation** — Produces interactive HTML ER diagrams with pan, zoom, drag, and search — no external dependencies
- **Variant Override System** — Per-class and per-property control over generated code, scoped to individual templates, via a declarative override/macro/dispatch pipeline

## Supported Databases

| Provider | Databases |
|----------|-----------|
| Microsoft.Data.SqlClient | SQL Server 2012+, Azure SQL Database, Azure SQL Managed Instance |
| Npgsql | PostgreSQL 10+, AWS Aurora PostgreSQL, Azure Database for PostgreSQL |
| MySqlConnector | MySQL 5.7+, MariaDB 10.2+, AWS Aurora MySQL, Azure Database for MySQL |
| Microsoft.Data.Sqlite | SQLite 3 |

## Quick Start

### 1. Register the MCP Server

Add pondhawk-mcp to your AI tool's MCP configuration:

```json
{
  "mcpServers": {
    "pondhawk": {
      "command": "pondhawk-persistence-mcp",
      "args": ["--project", "/path/to/your/project"]
    }
  }
}
```

### 2. Initialize a Project

Ask your AI agent to call the `init` tool:

```
Initialize pondhawk for a SQL Server database with namespace MyApp.Data
```

This creates:
- `persistence.project.json` — project configuration (single source of truth)
- `templates/entity.generated.liquid` — working entity template with dispatch macros
- `templates/entity.stub.liquid` — partial class stub (created once, never overwritten)
- `AGENTS.md` — comprehensive instructions for AI agents
- `.env` — database credentials (gitignored)
- JSON Schema files for IDE autocompletion

### 3. Introspect Your Database

```
Introspect the database schema
```

This connects to your database, reads all metadata, writes `db-design.json`, and auto-populates type mappings in your config.

### 4. Generate Code

```
Generate code for all tables
```

This renders your Liquid templates against the schema data and writes files to disk. No database connection needed — generation is purely file-driven from `db-design.json`.

## MCP Tools

| Tool | Description |
|------|-------------|
| `init` | Scaffolds a new project with config, templates, AGENTS.md, and .env |
| `introspect_schema` | Reads database schema into `db-design.json` and auto-populates type mappings |
| `generate` | Renders Liquid templates against schema data and writes generated files |
| `generate_ddl` | Generates dialect-specific DDL SQL from `db-design.json` |
| `generate_diagram` | Generates an interactive HTML ER diagram from `db-design.json` |
| `list_templates` | Lists all configured templates with their settings |
| `validate_config` | Validates project configuration without a database connection |
| `update` | Refreshes AGENTS.md and JSON schemas after a server upgrade |

## How It Works

### Partial Class Strategy

Each entity produces two files:

| File | Purpose | Overwrite Behavior |
|------|---------|-------------------|
| `Product.generated.cs` | Generated code from schema | Always overwritten |
| `Product.cs` | Developer stub for custom code | Only created if missing |

Developers extend entities in the stub file with custom logic, computed properties, and validation — these are never overwritten on regeneration.

### Variant Override System

The variant system provides precise control over generated code for specific classes and properties, scoped to individual templates:

**1. Define overrides** in `persistence.project.json`:
```json
{
  "Overrides": [
    { "Class": "*", "Property": "CreatedAt", "Artifact": "entity", "Variant": "AuditTimestamp" },
    { "Class": "Products", "Property": "Price", "Artifact": "entity", "Variant": "Currency" },
    { "Class": "Orders", "Artifact": "entity", "Variant": "SoftDelete" }
  ]
}
```

**2. Define macros** in Liquid templates:
```liquid
{%- macro DefaultProperty(a) %}
    public {{ a.ClrType | type_nullable: a.IsNullable }} {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}

{%- macro CurrencyProperty(a) %}
    [Column(TypeName = "decimal(18,2)")]
    public decimal {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}

{%- macro AuditTimestampProperty(a) %}
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime {{ a.Name | pascal_case }} { get; set; }
{%- endmacro %}
```

**3. Dispatch automatically** resolves and calls the right macro:
```liquid
{%- for a in entity.Attributes %}
{% dispatch a %}
{%- endfor %}
```

The same property can have different variants for different templates — `Products.Price` renders as `Currency` in the entity template but `FormattedCurrency` in the DTO template.

### Custom Liquid Filters

| Filter | Example | Output |
|--------|---------|--------|
| `pascal_case` | `{{ "order_item" \| pascal_case }}` | `OrderItem` |
| `camel_case` | `{{ "OrderItem" \| camel_case }}` | `orderItem` |
| `snake_case` | `{{ "OrderItem" \| snake_case }}` | `order_item` |
| `pluralize` | `{{ "Category" \| pluralize }}` | `Categories` |
| `singularize` | `{{ "Categories" \| singularize }}` | `Category` |
| `type_nullable` | `{{ a.ClrType \| type_nullable: a.IsNullable }}` | `int?` |

### Design-First Workflow

For new databases, the AI agent writes `db-design.json` directly with `"Origin": "design"`, then:

```
Generate DDL for PostgreSQL    →  db-design.postgresql.sql
Generate an ER diagram         →  db-design.html
```

Both tools work with any `db-design.json` — introspected or hand-designed.

## Project Configuration

All settings live in a single `persistence.project.json` file:

```json
{
  "Connection": {
    "Provider": "sqlserver",
    "ConnectionString": "${DB_CONNECTION}"
  },
  "OutputDir": "src/Data",
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
    }
  },
  "Defaults": {
    "Namespace": "MyApp.Data",
    "ContextName": "MyApp",
    "Schema": "dbo",
    "IncludeViews": false,
    "Include": ["Products", "Categories", "Orders*"],
    "Exclude": ["__EFMigrationsHistory"]
  },
  "DataTypes": {
    "Uid": { "ClrType": "string", "MaxLength": 28, "DefaultValue": "Ulid.NewUlid()" }
  },
  "TypeMappings": [
    { "DbType": "char(28)", "DataType": "Uid" }
  ],
  "Relationships": [
    {
      "DependentTable": "Products",
      "DependentColumns": ["CategoryId"],
      "PrincipalTable": "Categories",
      "PrincipalColumns": ["Id"]
    }
  ],
  "Overrides": [],
  "Logging": { "Enabled": false }
}
```

Connection strings support `${VAR}` substitution from `.env` files or system environment variables.

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build and Test

```bash
# Clean, restore, build, and test (default target)
dotnet run --project build

# Full pipeline including publish
dotnet run --project build -- --target=Publish
```

### Run Tests

Tests use xUnit v3 self-hosted executables. Always run with `dotnet run`:

```bash
dotnet run --project tests/Pondhawk.Persistence.Core.Tests --configuration Release
dotnet run --project tests/Pondhawk.Persistence.Mcp.Tests --configuration Release
```

All tests run without external database servers — SQLite in-memory databases are used for introspection and pipeline tests.

### Published Binaries

The `Publish` target produces self-contained single-file executables (no .NET runtime required):

| Platform | Binary |
|----------|--------|
| Windows x64 | `publish/win-x64/pondhawk-persistence-mcp.exe` |
| macOS ARM64 | `publish/osx-arm64/pondhawk-persistence-mcp` |
| Linux x64 | `publish/linux-x64/pondhawk-persistence-mcp` |
| Linux ARM64 | `publish/linux-arm64/pondhawk-persistence-mcp` |

## Architecture

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

The solution is split into two projects:

- **Pondhawk.Persistence.Core** — Class library with all core functionality (schema introspection, template rendering, DDL generation, diagram generation, caching, logging)
- **Pondhawk.Persistence.Mcp** — Thin MCP server layer that wraps core library methods as MCP tools

This separation allows the core library to be reused by other modalities (e.g., a CLI tool) without depending on MCP.

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10, C# 13 |
| MCP SDK | ModelContextProtocol |
| Template Engine | Fluid |
| DB Introspection | DatabaseSchemaReader |
| Configuration | System.Text.Json |
| Logging | Serilog + Serilog.Sinks.File + Serilog.Extensions.Logging |
| Build System | Cake Frosting |
| Test Framework | xUnit v3 + Shouldly + NSubstitute |
| Transport | stdio |

## License

See [LICENSE](LICENSE) for details.
