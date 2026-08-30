namespace Pondhawk.Generation.Mcp;

/// <summary>
/// Everything the server tells an agent about how to use it: the short orientation sent
/// in the MCP initialize handshake, and the full guide behind it.
/// </summary>
public static class AgentGuide
{
    /// <summary>
    /// URI the full guide is served under as an MCP resource.
    /// </summary>
    public const string ResourceUri = "pondhawk://agents.md";

    /// <summary>
    /// Sent to every client in the initialize handshake. This is the only documentation an
    /// agent is guaranteed to see, so it has to stand alone: a client that reads no resource
    /// and opens no file should still be able to work from this. It stays short because it
    /// occupies the client's context for the whole session -- the detail lives in
    /// <see cref="Markdown"/>.
    /// </summary>
    public static string ServerInstructions => $$"""
        pondhawk renders Liquid templates against a structured input model and writes the
        result to disk. Use it for sets of artifacts that must all follow the same pattern --
        entities, DTOs, clients, handlers -- where consistency across the set matters more
        than any single file. It knows nothing about databases, or C#, or any particular
        target: what comes out is decided entirely by the model and the templates.

        A project is three files you maintain and pondhawk only reads:

          model.json             what to generate -- nested nodes, each with a Name and a Kind
          templates/*.liquid     how to generate it -- one `Default<Kind>` macro per Kind
          pondhawk.project.json  which templates run, where output goes, and overrides

        `{% dispatch node %}` in a template calls the macro matching that node's Kind, which
        is what keeps a generated set uniform. An override can point a single node at a
        `<Variant><Kind>` macro instead.

        Nothing writes model.json for you after `init` -- edit it with ordinary file tools.
        Read it before extending it and reuse the Kinds and metadata keys already in use;
        introducing a second convention is the usual way a generated set stops being uniform.

        The loop is: edit the model or templates, run `validate_config`, then `generate`. Both
        are cheap and results are cached between calls, so run them freely while iterating.

        Two failure modes are quiet and worth guarding against explicitly:

          - `generate` can partially fail. Its result carries Success plus Created,
            Overwritten, Skipped and Failed counts. Check them; do not assume a returned
            result means every file was written.
          - A template that renders empty is skipped, and an unknown metadata key renders as
            nothing rather than failing. After a run that matters, open a generated file.

        `validate_config` catches the rest before anything is written -- unparseable
        templates, unknown filters, a model that violates its schema, overrides that match no
        node, and an override naming a variant macro no template declares.

        Full documentation: read the MCP resource {{ResourceUri}}. An initialized project also
        has the same text on disk as AGENTS.md.
        """;

    /// <summary>
    /// The full agent guide. Written to disk as AGENTS.md by init and update, and served
    /// over MCP as a resource so a client can read it without touching the filesystem.
    /// </summary>
    public static string Markdown => """
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

        ## The model is a project asset

        `model.json` is long-lived. It is committed alongside the templates and the config, it
        grows over time, and it is yours to maintain — pondhawk only ever reads it. Nothing
        writes it for you after `init`, so you edit it with ordinary file tools.

        That longevity is the point. The same model plus the same templates produce the same
        files on every run, which is what makes a generated set consistent rather than merely
        similar.

        Working on one carries obligations:

        - **Read the existing model before adding to it.** You are extending a document that
          already has conventions, not starting a new one.
        - **Reuse the Kinds already in use.** Adding a node of Kind `Field` to a model that
          says `Property` everywhere else creates a second convention and a second macro, and
          the set stops being uniform. The same goes for metadata keys: if existing properties
          carry `Type`, do not introduce `DataType`.
        - **A new Kind needs a new macro.** `{% dispatch %}` on a Kind with no
          `Default<Kind>` macro emits an error comment into the generated file rather than
          failing the run, so it is easy to miss.
        - **A misspelled `Variant` renders the default.** Dispatch falls back to
          `Default<Kind>` when `<Variant><Kind>` does not exist, so the file looks right and
          silently ignores the override. `validate_config` now reports this as an error.
        - **Node names become file paths.** A name containing `..` or a leading separator is
          refused rather than written outside the output directory.
        - **Renaming or removing a node can orphan an override.** Overrides address nodes by
          path, and a path that no longer matches silently stops applying. `validate_config`
          reports these, which is the main reason to run it after editing the model.

        ## Working on a pondhawk project

        1. Read `model.json` and the templates before changing either.
        2. Edit `model.json` — adding, changing, or removing nodes.
        3. Author or adjust templates, one `Default<Kind>` macro per Kind.
        4. Run `validate_config`. It reports unparseable templates, unknown filters, a model that
           violates its schema, overrides matching no node, templates whose `AppliesTo` matches no
           Kind in the model, and — the one that matters most — an override naming a variant macro
           the template does not declare.
        5. Run `generate`.
        6. Check `Success` in the `generate` result, then read a generated file. A template that
           renders empty is skipped silently, and an unknown metadata key renders as nothing
           rather than failing.

        Steps 4 and 5 are cheap and the model is cached between calls, so run them as often as
        you like while iterating.
        """;
}
