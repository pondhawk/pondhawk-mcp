using Pondhawk.Generation.Configuration;

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
        result to disk. It knows nothing about databases, or C#, or any particular target:
        what comes out is decided entirely by the model and the templates.

        WHY TO USE IT, AND WHEN NOT TO SKIP IT

        It is for work that is undifferentiated across many instances -- a fleet of entities,
        DTOs, clients, handlers, resources -- where there is one correct shape and no room for
        creativity in any individual file. On that work it beats writing the files yourself on
        every axis at once. Forty artifacts by hand is forty times the tokens and the wall
        clock; through a template it is one render. And the hand-written forty will not be
        identical -- they will be forty subtly different takes on one pattern, which is the
        actual defect, because being identical in shape was the whole point.

        The comparison to make is therefore not "generate this file, or write this file".
        Writing one file directly is genuinely cheaper, and that reasoning is a trap: the
        choice is between one template and N hand-written files that drift. Once a project
        generates a class of artifact, adding another member costs one entry in model.json --
        far less than writing the file -- and every member stays in step for free.

        So: before writing a file, check whether it belongs to a class this project already
        generates. `list_templates` gives the output patterns, `describe_model` gives the
        Kinds. If it does, add it to the model and run `generate`. Never hand-write a file
        this project generates, and never edit one after it is generated -- `generate` will
        overwrite it, and the work is lost. Change the model or the template instead.

        A project is three files you maintain and pondhawk only reads:

          model.json             what to generate -- nested nodes, each with a Name and a Kind
          templates/*.liquid     how to generate it -- one `Default<Kind>` macro per Kind
          pondhawk.project.json  which templates run, where output goes, and overrides

        A template reads model.json unless its `Model` field names another file, so unrelated
        concerns stay in separate models. `list_templates` reports which one each template reads.

        `{% dispatch node %}` in a template calls the macro matching that node's Kind, which
        is what keeps a generated set uniform. An override can point a single node at a
        `<Variant><Kind>` macro instead. Macros several templates share go in a file listed
        under `Partials`, which is joined ahead of every template -- Liquid's `{% include %}`
        will not do it, because the macro is discarded before dispatch looks for it.

        Nothing writes model.json for you after `init` -- edit it with ordinary file tools.
        Reuse the Kinds and metadata keys already in use; introducing a second convention is the
        usual way a generated set stops being uniform. Run `describe_model` before extending a
        model: it reports the Kind vocabulary, the metadata keys each Kind carries and how many
        nodes carry them, and flags inconsistencies, without your having to read the whole file.

        The loop is: edit the model or templates, iterate with `preview` (renders one node
        through one template and returns the text, writing nothing), run `validate_config`, then
        `generate` with `dryRun: true` to read the diffs, then `generate` for real. All are cheap and results
        are cached between calls, so run them freely while iterating. `check` answers a
        separate question -- are the files on disk already what the model produces -- and is
        what to run after pulling a branch.

        `generate` records what it wrote in `.pondhawk/manifest.json`, which is committed. That
        record lets `check` tell "the model changed" from "somebody edited a generated file",
        and lets `prune` delete files the model no longer produces without touching anything it
        did not write. `check` also reports files sitting in the output directory that pondhawk
        neither produces nor wrote -- usually a file someone hand-wrote where a generated one
        belongs. Its `Clean` field is the one to gate CI on.

        Two failure modes are quiet and worth guarding against explicitly:

          - `generate` can partially fail. Its result carries Success plus Created,
            Overwritten, Skipped and Failed counts. Check them; do not assume a returned
            result means every file was written.
          - A template that renders empty is skipped, and an unknown metadata key renders as
            nothing rather than failing. A dry run makes both visible before they reach disk;
            after a run that matters, open a generated file.

        `validate_config` catches the rest before anything is written -- unparseable
        templates, unknown filters, a model that violates its schema, overrides that match no
        node, and an override naming a variant macro no template declares.

        Full documentation: read the MCP resource {{ResourceUri}}. An initialized project has
        the same text on disk as AGENTS.md, beneath a preamble naming that project's own
        generated artifacts and where they live. Read that preamble before writing any file.
        """;

    /// <summary>
    /// A project-specific preamble written to the top of AGENTS.md.
    /// </summary>
    /// <remarks>
    /// The handshake instructions are read once, at connect time, and then compete with
    /// everything that happens afterwards. This is the same rule stated where the mistake
    /// actually gets made: in the repository, in the file coding agents read before touching
    /// anything. It names the real output directory and the real template list, because a rule
    /// that requires looking up where the generated files live is a rule that gets skipped.
    /// </remarks>
    public static string ProjectRules(ProjectConfiguration config)
    {
        var outputDir = string.IsNullOrWhiteSpace(config.OutputDir) ? "the output directory" : config.OutputDir;

        var artifacts = config.Templates.Count == 0
            ? "None configured yet."
            : string.Join("\n", config.Templates
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => $"| `{t.Key}` | `{t.Value.OutputPattern}` | {(t.Value.Mode.Equals("SkipExisting", StringComparison.OrdinalIgnoreCase) ? "written once, then yours" : "overwritten every run")} |"));

        return $"""
            # {config.ProjectName ?? "This project"} — rules for agents

            **This project generates code with pondhawk. Do not hand-write files it generates,
            and do not edit files it has generated.**

            Everything under `{outputDir}` is produced from `model.json` and the templates. A
            `generate` run overwrites it, so an edit made there is work waiting to be destroyed.
            To change generated output, change the model or the template and run `generate`.

            | Artifact | Produces | On each run |
            |----------|----------|-------------|
            {artifacts}

            Before writing a file anywhere, check whether it belongs to a class listed above. If
            it does, add a node to `model.json` and run `generate` instead — that is one line of
            model against a whole file written by hand, and it keeps the set consistent.

            Adding one more of something this project already generates is nearly free. Writing
            it by hand costs the file, and costs the uniformity that made generating it
            worthwhile.

            Run `check` to see whether the tree is current, and what is in `{outputDir}` that
            pondhawk did not put there.

            ---

            """;
    }

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
        | `model.json` | The default input model — what to generate |
        | `templates/*.liquid` | The templates — how to generate it |
        | `templates/_*.liquid` | Shared macros, if the config lists them under `Partials` |
        | `AGENTS.md` | This file |
        | `.pondhawk/manifest.json` | What pondhawk has written. Commit it |
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

        ### Sharing macros between templates

        A macro written in a template belongs to that template. When several artifacts render
        the same Kinds — an entity, a DTO, a validator all rendering `Property` — put the macros
        in a shared file and list it under `Partials`:

        ```json
        { "Partials": ["templates/_macros.liquid"] }
        ```

        Partials are joined onto the front of every template before it is parsed, in the order
        listed, with the template itself last. So a template that declares a macro of its own
        shadows the shared one: a project-wide default, overridable per artifact.

        This is what keeps the promise across a *set* of artifacts rather than within one file.
        Four templates each carrying their own copy of `DefaultProperty` drift apart, and the
        moment they do the fleet stops being uniform.

        Do not reach for Liquid's `{% include %}` for this. Fluid renders an include in a child
        scope, so a macro declared in the included file is discarded before `{% dispatch %}`
        looks for it — the include succeeds, the macro is not found, and the file fails to
        render. `Partials` is the mechanism that works.

        One edit to a shared macro changes every artifact that uses it, which is the point and
        also the risk: run `generate` with `dryRun: true` afterwards and read the diffs.

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
        - **Model** — the model file this template reads. Omit for `model.json`.
        - **Partials** — top level, not per template. Liquid files whose macros every template shares.
        - **Values** — anything templates need. String values support `${VAR}` from `.env`.

        ### More than one model

        A project with unrelated generation concerns keeps them in separate models rather than
        splicing both into one document, and each template says which one it reads:

        ```json
        "Templates": {
          "entity":     { "Path": "templates/entity.liquid", "AppliesTo": "Class" },
          "api-client": { "Path": "templates/client.liquid", "Model": "api.model.json" }
        }
        ```

        Each model is a whole document: its own `Name`, its own root metadata, its own Kind
        vocabulary. `{{ model }}` in a template is the root of the model *that* template reads.
        Two concerns sharing one file would have to share one root, and a model regenerated from
        an upstream source — an OpenAPI document, a database — would have to be spliced into a
        file that also holds hand-written content.

        Nothing forces the split. A single `model.json` partitioned by `AppliesTo` is the right
        answer while the concerns are small and stable; reach for a second model when they have
        genuinely different lifecycles.

        ### The two-file pattern

        Pair an `Always` template with a `SkipExisting` one to separate generated code from
        hand-written code. The generated file is overwritten freely; the stub is created once and
        is then the developer's.

        What the two halves *are* depends on how much the target language lets generated and
        hand-written code contribute to one type:

        | | Mechanism | Languages |
        |---|---|---|
        | The type itself splits | `partial class` — neither half is privileged | C#, VB.NET |
        | The type is declared once, behavior attaches from elsewhere | Methods elsewhere in the same package, extra `impl` blocks, extensions, categories, open classes, traits | Go, Rust, Swift, Objective-C, Kotlin, Scala, Ruby, Python, PHP, Dart |
        | Neither — inherit instead | Generated base class, hand-written subclass | Java, and the fallback anywhere else |

        Most of the middle row adds behavior but not state: Swift extensions declare no stored
        properties, and Go and Rust accept fields only at the type's own declaration. So let the
        generated half own the data and the hand-written half add methods — which is the division
        you want regardless.

        Two things worth knowing when you meet them. Dart's `part` / `part of` is the closest
        analogue to the C# workflow outside .NET and is what `json_serializable` and `freezed`
        build on, but it splits a *library* rather than a class, so the generated file supplies
        mixins or extensions. And TypeScript's `Partial<T>` is unrelated — a mapped type that
        makes properties optional; TypeScript's actual analogue is declaration merging, which
        merges interfaces but not classes.

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
        | `init` | Scaffolds a new project. Its example templates render Markdown and exist to show the mechanics, not to be edited into your real ones |
        | `generate` | Renders templates and writes files; `dryRun: true` reports what would change instead |
        | `check` | Reports whether the files on disk are what the model and templates produce |
        | `prune` | Removes generated files the model no longer produces. Reports unless told to apply |
        | `list_templates` | Lists configured templates |
        | `describe_model` | Summarises a model's Kinds, metadata keys and inconsistencies |
        | `preview` | Renders one template for one node and returns the text, writing nothing |
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
          already has conventions, not starting a new one. `describe_model` reports them
          directly, which is cheaper and more reliable than skimming the file.
        - **Kinds are case-sensitive where it counts.** A template's `AppliesTo` matches a Kind
          case-insensitively, but dispatch builds the macro name from the literal Kind, so
          `Class` and `class` resolve to `DefaultClass` and `Defaultclass` — two different
          macros. `describe_model` reports the pair as a notice.
        - **Reuse the Kinds already in use.** Adding a node of Kind `Field` to a model that
          says `Property` everywhere else creates a second convention and a second macro, and
          the set stops being uniform. The same goes for metadata keys: if existing properties
          carry `Type`, do not introduce `DataType`.
        - **A new Kind needs a new macro.** `validate_config` warns when a Kind beneath the
          nodes a template renders has no macro, which is the cheap moment to find out.
          Otherwise `{% dispatch %}` on a Kind with no `Default<Kind>` macro fails the file: it is not written, `generate` counts it under
          `Failed`, and `Success` is false. Every unresolved node in that file is reported at
          once, so a Kind missing its macro is one message rather than a queue of them.
        - **A misspelled `Variant` renders the default.** Dispatch falls back to
          `Default<Kind>` when `<Variant><Kind>` does not exist, so the file looks right and
          silently ignores the override. `validate_config` now reports this as an error.
        - **Node names become file paths.** A name containing `..` or a leading separator is
          refused rather than written outside the output directory.
        - **Renaming or removing a node can orphan an override.** Overrides address nodes by
          path, and a path that no longer matches silently stops applying. `validate_config`
          reports these, which is the main reason to run it after editing the model.

        ## Working on a pondhawk project

        1. Run `describe_model`, then read the templates. The description gives you the Kind
           vocabulary, which Kinds nest inside which, the metadata keys each Kind carries and
           how many nodes carry them — the conventions you have to match — without reading a
           model that may be hundreds of nodes long. Read its `Notices` first; they are where a
           second convention already creeping in shows up.
        2. Edit `model.json` — adding, changing, or removing nodes.
        3. Author or adjust templates, one `Default<Kind>` macro per Kind. Iterate with
           `preview`, which renders a single node through a single template and hands back the
           text — overrides and variants applied, nothing written. A render error comes back as
           `Error` rather than failing the call, because a half-finished macro is the normal
           state while writing one.
        4. Run `validate_config`. It reports unparseable templates, unknown filters, a model that
           violates its schema, overrides matching no node, templates whose `AppliesTo` matches no
           Kind in the model, a Kind nested under what a template renders that has no
           `Default<Kind>` macro to render it, and — the one that matters most — an override
           naming a variant macro the template does not declare.
        5. Run `generate` with `dryRun: true` and read the diffs. Nothing is written. This is
           where a macro change shows its blast radius before you accept it, and where the two
           quiet failures become visible: a template that renders empty appears as
           `WouldSkipEmpty` rather than vanishing, and a metadata key that resolves to nothing
           shows up as a hole in the diff.
        6. Run `generate`.
        7. Check `Success` in the result, then read a generated file. A file that could not be
           rendered is never written — a broken artifact does not reach disk — but an empty
           render still writes nothing quietly, and an unknown metadata key still renders as
           nothing rather than failing.

        Steps 4 to 6 are cheap and the model is cached between calls, so run them as often as
        you like while iterating.

        `preview` and `generate --dryRun` answer different questions. `preview` is "what does
        this one artifact look like right now", and is the loop to sit in while writing a macro.
        A dry run is "what does this change do to the whole project", and is what to read before
        accepting an edit to something shared.

        ## The manifest

        `generate` records what it wrote in `.pondhawk/manifest.json` — for each file, the
        template and node that produced it, the model, and a hash of the content written.
        **Commit it.** Its whole value is knowing what happened in a tree you did not generate
        yourself, so a copy that only exists on the machine that wrote it is worth little.
        Regenerating an unchanged project leaves it byte-identical, so it stays quiet in
        `git status`, and `.pondhawk/.gitignore` keeps the logs beside it out of version control.

        It is a snapshot of the output tree, not a log of runs — every question asked of it is
        about now, and git already keeps the history. Two consequences worth knowing:

        - A run naming specific templates or items **merges** into the manifest rather than
          replacing it, so generating one artifact does not orphan the others.
        - An entry for a file that is no longer produced is **kept**. That entry is the only
          evidence pondhawk wrote the file, and it is what lets `prune` delete it safely. Only
          `prune` removes entries.

        The hash separates two situations a content comparison cannot tell apart. When a
        generated file differs from what the templates now produce, `check` reports
        `InputsChanged` if the file is exactly as pondhawk left it — safe to regenerate — and
        `EditedSinceGenerated` if it is not. The second means someone put work into a generated
        file and regenerating will discard it; move that work into the template, or into a
        `SkipExisting` file, before running `generate`.

        `prune` deletes only files it can prove it owns: recorded in the manifest, still byte
        for byte as pondhawk wrote them, and not `SkipExisting`. Anything else it reports and
        leaves alone. It reports without deleting unless you pass `apply: true`.

        ## Checking a project you did not generate

        `check` answers a different question: are the files on disk already what the model
        produces? Run it after pulling a branch, or before trusting generated code you did not
        just generate. It writes nothing, reports every stale file with a reason — `Missing`,
        `InputsChanged`, `EditedSinceGenerated` — and lists orphans the manifest records but the
        configuration no longer produces. No diffs; use `generate` with `dryRun` for those. A
        `SkipExisting` stub that exists is never stale, because `generate` would not touch it.
        """;
}
