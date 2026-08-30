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

## Reading a model you did not write

`describe_model` reports a model's conventions rather than its contents: the Kind vocabulary with
counts and example names, which Kinds nest inside which, the metadata keys each Kind carries and
how many of its nodes carry them, and notices about inconsistencies. It reports counts and
vocabularies, never node listings, so a five-hundred-node model summarises to about the size of a
twenty-node one.

The notices are the point. Extending a model means matching what is already there, and the ways
that quietly goes wrong are recognisable:

- `DataType` on 2 of 44 nodes beside `Type` on 44 of 44 — a second name for one concept.
- `Property` beside `Properties`, or a Kind that is a one-character typo of another.
- `Class` beside `class` — `AppliesTo` matches case-insensitively, but dispatch builds the macro
  name from the literal Kind, so those resolve to `DefaultClass` and `Defaultclass`.
- One metadata key holding both `boolean` and `string` values.

A sparse key with no similarly-named rival is not flagged; an optional flag on a few nodes is
ordinary modelling.

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
Initialize a pondhawk project
```

`init` creates `pondhawk.project.json`, a starter `model.json`, two example templates, JSON schemas for IDE autocompletion, `AGENTS.md`, and `.env`.

The examples render Markdown, and deliberately so. They exist to demonstrate the mechanics — a macro per Kind, `{% dispatch %}`, a filter, a config value, and the two-file pattern — in a format that is nobody's real target. A scaffold that looked like C# would be a starter template pack by another name: frozen at the moment the binary was built, and inviting you to edit it into production rather than replace it. Run `generate` once to watch it work, then write templates for your own target.

### 3. Describe what to generate

Replace the starter nodes and their Kinds with your own, then author templates with one `Default<Kind>` macro per Kind.

### 4. Generate

```
Validate the config, then generate
```

To check generated files are current without a client — in CI, or after pulling a branch:

```bash
pondhawk-generation-mcp --project . --check
```

`validate_config` reports unparseable templates, unknown filters, a model violating its schema, overrides matching no node, templates whose `AppliesTo` matches no kind in the model, a Kind nested under what a template renders that has no `Default<Kind>` macro, and overrides naming a variant macro no template declares.

## MCP Tools

| Tool | Description |
|------|-------------|
| `init` | Scaffolds a new project: config, a starter model, two Markdown example templates demonstrating the mechanics, schemas, and AGENTS.md |
| `generate` | Renders templates against the model and writes files; `dryRun: true` reports what would change instead, with unified diffs, and writes nothing |
| `check` | Reports whether the files on disk are what the model and templates currently produce, plus orphans and untracked files. `Clean` is the field to gate CI on |
| `prune` | Removes generated files the model no longer produces; reports unless passed `apply` |
| `list_templates` | Lists configured templates with their settings |
| `describe_model` | Summarises a model's Kinds, structure, metadata keys and inconsistencies without listing its nodes |
| `preview` | Renders one template for one node and returns the text, writing nothing |
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

## Two ways to look before you write

`preview` renders a single node through a single template and returns the text. Overrides and
variants apply exactly as in a real run — it plans through the same code, and a test pins its
output against what `generate` subsequently writes. A render error comes back as `Error` rather
than failing the call, because a half-finished macro is the normal state while authoring one.

`generate` with `dryRun: true` renders everything and returns unified diffs against what is on
disk.

They answer different questions. `preview` is "what does this one artifact look like right now",
and is the loop to sit in while writing a macro. A dry run is "what does this change do across
the project", and is what to read before accepting an edit to something shared — a partial,
especially.

## Why generate at all

pondhawk earns its place on work that is undifferentiated across many instances — a fleet of
entities, DTOs, clients, handlers — where there is one correct shape and no room for creativity
in any individual file. On that work it wins on every axis at once: forty artifacts by hand is
forty times the tokens and the wall clock, and the forty will not come out identical. They will
be forty subtly different takes on one pattern, which is the actual defect, because being
identical in shape was the point.

The comparison that matters is not "generate this file or write this file" — writing one file
directly really is cheaper, and that reasoning is the trap. The choice is between one template
and N hand-written files that drift. Once a project generates a class of artifact, adding
another member costs one entry in `model.json`, which is less than writing the file, and every
member stays in step for free.

Three things keep that from eroding:

- **The handshake instructions** say it to every client before it does anything, including the
  rule: check whether a file belongs to a generated class before writing it, and never edit a
  generated file.
- **`AGENTS.md`** carries a project-specific preamble naming that project's artifacts and output
  directory, so the rule is present in the repository where the mistake actually happens.
- **`check`** makes bypassing it visible. Its `Clean` field covers stale files, orphans, and
  untracked files sitting in the output directory that pondhawk neither produces nor wrote —
  which is the shape of someone hand-writing a file where a generated one belongs. Gate CI on
  it; documentation persuades, a failing build is what holds.

### Gating CI

The server binary runs the check from a shell and reports through its exit code, so no MCP
client is needed:

```bash
pondhawk-generation-mcp --project . --check
```

`0` clean, `1` not clean, `2` could not run — a broken project and a dirty one are different
answers, and a build script should be able to tell them apart. In GitHub Actions:

```yaml
- name: Generated code is current
  run: pondhawk-generation-mcp --project . --check
```

It fails on a stale file, a file edited after generation, a file the model no longer produces,
and a file someone hand-wrote in the output directory.

## The manifest

`generate` records what it wrote in `.pondhawk/manifest.json` — per file, the template and node
that produced it, the model, and a hash of the content. **Commit it.** Its value is knowing what
happened in a tree you did not generate yourself, so a copy confined to the machine that wrote it
is worth little. Regenerating an unchanged project leaves it byte-identical — no timestamps, no
run counters — so it stays quiet in `git status`, and `.pondhawk/.gitignore` keeps the log
directory beside it out of version control.

It is a snapshot of the output tree, not a log of runs; git already keeps the history. A filtered
run merges into it rather than replacing it, and an entry for a file no longer produced is kept —
that entry is the only evidence pondhawk wrote the file, and it is what makes safe deletion
possible. Only `prune` removes entries.

The hash separates two things a content comparison cannot:

| File vs manifest | File vs freshly rendered | `check` reports | Meaning |
|---|---|---|---|
| same | differs | `InputsChanged` | The model or template moved on. Safe to regenerate. |
| differs | differs | `EditedSinceGenerated` | Someone edited generated output. Regenerating discards it. |

`prune` deletes only what it can prove it owns: recorded in the manifest, byte for byte as
pondhawk wrote it, and not `SkipExisting`. Everything else it reports and leaves alone.

### If the project runs a formatter

Keep it away from generated files. A formatter changes the bytes, which is indistinguishable
from a hand edit — `check` reports `EditedSinceGenerated`, the next `generate` reverts the
formatting, the formatter reapplies it, and the two tools fight over the file indefinitely.

Make the template emit output the formatter would accept instead: a one-time cost per template
rather than a permanent conflict. `preview` renders one node and returns the text without
writing, which is the loop for getting whitespace right.

pondhawk runs no external commands, by design — see the roadmap for why formatting hooks were
declined.

## Configuration

All settings live in `pondhawk.project.json`:

```json
{
  "$schema": "./pondhawk.project.schema.json",
  "OutputDir": "generated",
  "Templates": {
    "reference": {
      "Path": "templates/reference.liquid",
      "OutputPattern": "{{ item.Name | pascal_case }}.generated.md",
      "Scope": "PerItem",
      "Mode": "Always",
      "AppliesTo": "Section"
    },
    "notes": {
      "Path": "templates/notes.liquid",
      "OutputPattern": "{{ item.Name | pascal_case }}.notes.md",
      "Scope": "PerItem",
      "Mode": "SkipExisting",
      "AppliesTo": "Section"
    }
  },
  "Values": { "Owner": "MyTeam" },
  "Overrides": [],
  "Logging": { "Enabled": false }
}
```

- **Scope** — `PerItem` renders one file per matching node; `Single` renders one file for all.
- **Mode** — `Always` overwrites every run; `SkipExisting` writes once and then leaves the file alone.
- **AppliesTo** — restricts a template to top-level nodes of one `Kind`. Omit for all.
- **Model** — the model file this template reads. Omit for `model.json`.
- **Partials** — top level, not per template. Liquid files whose macros every template shares.
- **Values** — anything templates need, as `{{ values.X }}`. String values support `${VAR}` substitution from `.env`.

A node whose `Kind` resolves to no macro fails its file rather than marking it: the file is not
written, `generate` counts it under `Failed`, and `Success` is false. Every unresolved node in
one file is reported together. Nothing partial or annotated reaches disk.

Rendered output paths are confined to `OutputDir`. A node name containing `..` or a leading separator is refused rather than written elsewhere, and `generate` returns `Success: false` with the offending file listed.

### Sharing macros between templates

A macro written in a template belongs to that template, so several artifacts rendering the same
Kinds each end up with their own copy of `DefaultProperty` — and the moment those drift, the
fleet stops being uniform in exactly the way this tool exists to prevent. Put them in a shared
file instead:

```json
{ "Partials": ["templates/_macros.liquid"] }
```

Partials are joined onto the front of every template before parsing, in the order listed, with
the template last — so a template declaring the same macro shadows the shared one. A
project-wide default, overridable per artifact.

Liquid's `{% include %}` does not work for this, and deliberately is not wired up: Fluid renders
an include in a child scope, so a macro declared in the included file is discarded before
`{% dispatch %}` looks for it. The include succeeds, the macro is not found, and an error
comment lands in the generated file — a silent-looking failure of exactly the kind the rest of
this tool works to eliminate.

One edit to a shared macro changes every artifact that uses it. Run `generate` with
`dryRun: true` to see how far it reaches before accepting it.

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

What the two halves *are* depends on how much the target language lets generated and
hand-written code contribute to one type:

| | Mechanism | Languages |
|---|---|---|
| The type itself splits | `partial class` — neither half is privileged | C#, VB.NET |
| The type is declared once, behavior attaches from elsewhere | Methods elsewhere in the same package, extra `impl` blocks, extensions, categories, open classes, traits | Go, Rust, Swift, Objective-C, Kotlin, Scala, Ruby, Python, PHP, Dart |
| Neither — inherit instead | Generated base class, hand-written subclass | Java, and the fallback anywhere else |

Most of the middle row adds behavior but not state — Swift extensions declare no stored
properties, and Go and Rust accept fields only at the type's own declaration — so the generated
half owns the data and the hand-written half adds methods.

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
