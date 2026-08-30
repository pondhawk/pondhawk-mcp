# pondhawk-generation

An MCP server that renders Liquid templates against a structured input model and writes the result to disk. Built with C# on .NET 10.

It exists for artifacts that must all follow the **same** pattern — a fleet of entities, DTOs, API clients, command handlers, resources — where consistency across the set matters more than any individual file. Every node of a given kind renders through one macro, so changing that macro changes every artifact at once.

pondhawk knows nothing about databases, or C#, or any particular target. What it generates is determined entirely by the model you write and the templates you author. Templates are primarily authored and maintained by AI agents as part of the development workflow.

## Why a generator at all

Code generation only earns its keep when there is repetition. A one-off class you write by hand, or have an agent write directly. The moment it is worth generating, there is a list driving a loop — and that list is what pondhawk takes as input.

## The input model

`model.json` holds a tree of nodes. Every node has a `Name` and a `Kind`, and may have `Children`. Everything else you write on a node is metadata, reached from templates as an ordinary member.

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

`"Type": "decimal"` is read as `{{ p.Type }}` — no `Metadata.` prefix, and the engine never needs to know the shape of your model.

Nodes nest to any depth. Two levels suit a class with properties; three suit a resource whose operations have parameters:

| Artifact | Level 1 | Level 2 | Level 3 |
|---|---|---|---|
| Entity | class | property | — |
| API client | resource | operation | parameter |
| gRPC service | service | rpc | message field |
| Command handlers | aggregate | command | field |

You write the model, or an agent does. To generate from an existing database, pair pondhawk with a schema-aware MCP server and have the agent feed one from the other — pondhawk does not connect to databases itself.

## Dispatch

`Kind` selects the macro that renders a node. Write one `Default<Kind>` macro per kind:

```liquid
{%- macro DefaultClass(c) %}
public partial class {{ c.Name | pascal_case }}
{%- endmacro %}

{%- macro DefaultProperty(p) %}
    public {{ p.Type | type_nullable: p.IsNullable }} {{ p.Name | pascal_case }} { get; set; }
{%- endmacro %}

{% dispatch item %}
{
{%- for p in item.Children %}
{% dispatch p %}
{%- endfor %}
}
```

`{% dispatch p %}` looks at the node's `Kind` and calls the matching macro. This is the consistency guarantee: every node of a kind goes through one macro.

### Variants

When one node needs to render differently, define a `<Variant><Kind>` macro and point an override at it:

```liquid
{%- macro CurrencyProperty(p) %}
    [Column(TypeName = "decimal(18,2)")]
    public decimal {{ p.Name | pascal_case }} { get; set; }
{%- endmacro %}
```

```json
{ "Path": "Product/Price", "Artifact": "entity", "Variant": "Currency" }
```

Dispatch falls back to `Default<Kind>` when a variant macro is missing, so an override naming a macro you have not written yet degrades rather than breaking the run. That fallback is also silent, so `validate_config` reports a variant with no matching macro as an error — otherwise a misspelled `Variant` produces a file that looks correct and ignores the override.

## Overrides

Overrides address nodes by path and change how they render for one artifact.

```json
{ "Path": "Product/Price", "Artifact": "entity", "Variant": "Currency" }
{ "Path": "*/CreatedAt",   "Artifact": "entity", "Ignore": true }
{ "Path": "Order/**",      "Artifact": "dto",    "Metadata": { "Access": "internal" } }
```

- `Path` — slash-delimited. `*` matches one node, `**` matches any depth.
- `Artifact` — the template key. Required with `Variant`; omit to apply everywhere.
- `Variant` — the macro variant to render with.
- `Ignore` — drops matched nodes from that artifact.
- `Metadata` — merged onto matched nodes, overwriting what the model declared.

When several overrides match one node, the one with the **most literal path segments** wins. Ties go to whichever is listed later, so state the broad rule first and narrow afterwards.

## Quick Start

### 1. Register the MCP server

```json
{
  "mcpServers": {
    "pondhawk": {
      "command": "pondhawk-generation-mcp",
      "args": ["--project", "/path/to/your/project"]
    }
  }
}
```

### 2. Scaffold a project

```
Initialize a pondhawk project with namespace MyApp.Data
```

The `init` tool creates `pondhawk.project.json`, a starter `model.json`, example templates, JSON schemas for IDE autocompletion, `AGENTS.md`, and `.env`.

### 3. Describe what to generate

Edit `model.json`, then author templates with one `Default<Kind>` macro per kind.

### 4. Generate

```
Validate the config, then generate
```

`validate_config` reports unparseable templates, unknown filters, a model violating its schema, overrides matching no node, templates whose `AppliesTo` matches no kind in the model, and overrides naming a variant macro no template declares — all of which otherwise produce wrong output silently.

## MCP Tools

| Tool | Description |
|------|-------------|
| `init` | Scaffolds a new project with config, model, templates, schemas, and AGENTS.md |
| `generate` | Renders templates against `model.json` and writes files |
| `list_templates` | Lists configured templates with their settings |
| `validate_config` | Checks config, templates, and model without generating |
| `update` | Refreshes AGENTS.md and JSON schemas after a server upgrade |

## What the server tells an agent

An agent does not have to be told how to drive pondhawk — the server documents itself over
the protocol, so a bare MCP connection is enough to work from.

| Channel | Contents |
|---------|----------|
| `instructions` in the initialize handshake | A short orientation: the three project files, dispatch, the validate/generate loop, and the two failure modes that are quiet |
| Resource `pondhawk://agents.md` | The full guide — the input model, macros and dispatch, variants, configuration, overrides, and the working loop |
| Tool descriptions | What each tool does and what it returns |

The resource is served from the binary rather than read off disk, so it always matches the
running server and is readable before `init` has created anything. `init` and `update` write
the same text into the project as `AGENTS.md` for people and for file-based coding agents.

## Configuration

All settings live in `pondhawk.project.json`:

```json
{
  "$schema": "./pondhawk.project.schema.json",
  "OutputDir": "src/Generated",
  "Templates": {
    "entity": {
      "Path": "templates/entity.generated.liquid",
      "OutputPattern": "{{ item.Name | pascal_case }}.generated.cs",
      "Scope": "PerItem",
      "Mode": "Always",
      "AppliesTo": "Class"
    },
    "entity-stub": {
      "Path": "templates/entity.stub.liquid",
      "OutputPattern": "{{ item.Name | pascal_case }}.cs",
      "Scope": "PerItem",
      "Mode": "SkipExisting",
      "AppliesTo": "Class"
    }
  },
  "Values": { "Namespace": "MyApp.Data" },
  "Overrides": [],
  "Logging": { "Enabled": false }
}
```

- **Scope** — `PerItem` renders one file per matching node; `Single` renders one file for all.
- **Mode** — `Always` overwrites every run; `SkipExisting` writes once and then leaves the file alone.
- **AppliesTo** — restricts a template to top-level nodes of one `Kind`. Omit for all.
- **Model** — the model file this template reads. Omit for `model.json`.
- **Values** — anything templates need, as `{{ values.X }}`. String values support `${VAR}` substitution from `.env`.

Rendered output paths are confined to `OutputDir`. A node name containing `..` or a leading separator is refused rather than written elsewhere, and `generate` returns `Success: false` with the offending file listed.

### More than one model

A project with unrelated generation concerns keeps them in separate models rather than splicing
both into one document. Each template names the one it reads:

```json
"Templates": {
  "entity":     { "Path": "templates/entity.liquid", "AppliesTo": "Class" },
  "api-client": { "Path": "templates/client.liquid", "Model": "api.model.json" }
}
```

Each model is a whole document — its own `Name`, its own root metadata, its own `Kind`
vocabulary — and `{{ model }}` in a template is the root of the one that template reads. The
case this exists for is divergent lifecycles: an entity model edited by hand and an API model
regenerated from an OpenAPI document have no business sharing a file, because the regeneration
would have to splice into hand-written content.

Nothing forces the split. A single `model.json` partitioned by `AppliesTo` is the right answer
while the concerns are small and stable.

### The two-file pattern

Pair an `Always` template with a `SkipExisting` one to separate generated code from hand-written code:

| File | Purpose | Overwrite |
|------|---------|-----------|
| `Product.generated.cs` | Generated from the model | Always |
| `Product.cs` | Developer's own code | Only created if missing |

In C# these are `partial class` halves; other languages have their own equivalents.

## Template Context

| Variable | Contents |
|----------|----------|
| `item` | The current node (`PerItem` scope) |
| `items` | All matching nodes (`Single` scope) |
| `model` | The model root — `{{ model.Name }}` plus root metadata |
| `values` | The `Values` section of the config |
| `config` | The project configuration |
| `parameters` | Key-values passed to the `generate` call |

### Custom Filters

| Filter | Example | Output |
|--------|---------|--------|
| `pascal_case` | `{{ "order_item" \| pascal_case }}` | `OrderItem` |
| `camel_case` | `{{ "OrderItem" \| camel_case }}` | `orderItem` |
| `snake_case` | `{{ "OrderItem" \| snake_case }}` | `order_item` |
| `pluralize` | `{{ "Category" \| pluralize }}` | `Categories` |
| `singularize` | `{{ "Categories" \| singularize }}` | `Category` |
| `type_nullable` | `{{ p.Type \| type_nullable: p.IsNullable }}` | `int?` |

Plus Liquid's built-ins, via [Fluid](https://github.com/sebastienros/fluid).

## Example: a non-C# artifact

A three-level model producing TypeScript API clients:

```json
{
  "Name": "BillingApi",
  "BaseUrl": "https://api.example.com/v1",
  "Nodes": [
    { "Name": "Invoices", "Kind": "Resource", "Route": "/invoices", "Children": [
      { "Name": "list", "Kind": "Operation", "Verb": "GET", "Returns": "Invoice[]", "Children": [
        { "Name": "page", "Kind": "Parameter", "Type": "number", "Required": false }
      ]}
    ]}
  ]
}
```

```typescript
export class InvoicesClient {
  async list(page?: number): Promise<Invoice[]> {
    return request("GET", BASE + "/invoices");
  }
}
```

Same engine, same dispatch, no C# and no database anywhere in sight.

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
dotnet run --project tests/Pondhawk.Generation.Tests --configuration Release
dotnet run --project tests/Pondhawk.Generation.Mcp.Tests --configuration Release
```

### Coverage

```bash
dotnet run --project build -- --target=Coverage
```

Runs both suites under a coverage collector, merges the results, and writes an HTML
report to `coverage/report/index.html` alongside a summary printed to the console.
The collector and report generator are pinned in `dotnet-tools.json` and restored
automatically, so no global install is needed.

Pass `--threshold=N` to fail the build when line coverage drops below `N` percent:

```bash
dotnet run --project build -- --target=Coverage --threshold=90
```

### Published Binaries

The `Publish` target produces self-contained single-file executables (no .NET runtime required):

| Platform | Binary |
|----------|--------|
| Windows x64 | `publish/win-x64/pondhawk-generation-mcp.exe` |
| macOS ARM64 | `publish/osx-arm64/pondhawk-generation-mcp` |
| Linux x64 | `publish/linux-x64/pondhawk-generation-mcp` |
| Linux ARM64 | `publish/linux-arm64/pondhawk-generation-mcp` |

## Architecture

```
┌─────────────┐       stdio         ┌──────────────────────────────┐
│  AI Agent   │◄───────────────────►│   pondhawk-generation-mcp    │
│ (Claude,    │   MCP Protocol      │                              │
│  Copilot)   │                     │  ┌────────────────────────┐  │
└─────────────┘                     │  │     MCP Tool Layer     │  │
                                    │  └───────────┬────────────┘  │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   Model Loader         │  │
                                    │  │   model.json → Nodes   │  │
                                    │  └───────────┬────────────┘  │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   Override Resolver    │  │
                                    │  │   paths → variants     │  │
                                    │  └───────────┬────────────┘  │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   Template Engine      │  │
                                    │  │   Fluid + dispatch     │  │
                                    │  └───────────┬────────────┘  │
                                    │  ┌───────────┴────────────┐  │
                                    │  │   File Writer          │  │
                                    │  └────────────────────────┘  │
                                    └──────────────────────────────┘
```

Two projects:

- **Pondhawk.Generation** — the engine: model loading, override resolution, template rendering, file writing, caching, logging
- **Pondhawk.Generation.Mcp** — a thin MCP server wrapping the engine as tools

The split keeps the engine modality-agnostic, so a CLI can sit beside the MCP server without duplicating it.

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10, C# 13 |
| MCP SDK | ModelContextProtocol |
| Template Engine | Fluid |
| Configuration | System.Text.Json |
| Schema Validation | JsonSchema.Net |
| Logging | Serilog |
| Build System | Cake Frosting |
| Test Framework | xUnit v3 + Shouldly + NSubstitute |
| Transport | stdio |

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

**Note:** The GPL applies to the pondhawk-generation tool itself. Code generated by the tool from your templates and models is your own and is not covered by this license.
