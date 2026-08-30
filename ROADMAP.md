# Roadmap

Five things that would make pondhawk the strongest general code generation server, in the
order they should be built. Each entry records *why*, because the reasoning is the part that
gets lost.

Status: `[ ]` not started · `[~]` in progress · `[x]` done

---

## 1. Dry run and drift

`[ ]` **`generate(dryRun: true)`** — render to memory, diff against disk, write nothing.
`[ ]` **`check`** — is the checked-in generated code what the model currently produces?

One engine, two tools. Render-to-memory plus compare-to-disk answers both questions; they
differ only in who is asking and what shape of answer they want.

**Why.** The write side is the least defended part of the pipeline: `validate_config` never
renders, `generate` renders and writes, and there is nothing in between. A dry run is how you
change one macro and see its effect across a fleet before committing to it, and it makes the
two quiet failures the guide warns about — a template that renders empty, a metadata key that
renders as nothing — *visible* rather than merely documented. `check` is the CI gate: it tells
an agent that just pulled a branch whether the generated tree is stale.

**Prerequisite.** Output must be byte-stable across runs or both features are noise. Audit for
timestamps, machine paths, and any non-deterministic ordering before building on it.

**Constraint.** The dry run must resolve output paths through the same code as the real write,
including the escape refusal — a preview that shows a file the real run would reject is worse
than no preview.

## 2. A manifest

`[ ]` Record what was written: path, template key, source node, content hash
(`.pondhawk/manifest.json`).

**Why.** Small, and it makes three currently-impossible things possible:

- **Orphan cleanup.** Delete a node from the model today and its generated file lives on disk
  forever. A manifest lets the tool report — and safely remove — files it produced and no
  longer produces. Safe because it only ever touches files it recorded, whose hash still
  matches; never a `SkipExisting` stub, never a hand-edited file.
- **Hand-edit detection.** An `Always` file whose hash has drifted means someone edited
  generated output and is about to lose that work. There is nowhere for that warning to come
  from today.
- **Incremental generation.** Skip files whose model subtree, template and values are all
  unchanged. The natural payoff of the timestamp cache that already exists.

## 3. Shared macros

`[ ]` Configure a Fluid `FileProvider` rooted at `templates/`, enabling `{% include %}` and a
shared partials file.

**Why.** This is the gap that undermines the core promise. "Every node of a Kind goes through
one macro, so changing that macro changes every artifact at once" holds *within* a template
file. A project with entity, DTO, validator and mapper templates duplicates `DefaultProperty`
four times, and the moment those drift the fleet stops being uniform in exactly the way this
tool exists to prevent. `TemplateOptions` sets no `FileProvider` today, so `{% include %}` and
`{% render %}` are not wired up at all.

## 4. `describe_model`

`[ ]` Report the Kind vocabulary, metadata keys observed per Kind with counts, node counts and
tree depth.

**Why.** The guide tells an agent to reuse the Kinds already in use, and not to introduce
`DataType` beside an existing `Type` — then gives it no way to see either without reading the
whole model. On a large model that is expensive and easy to skim. This turns a stated
obligation into something checkable.

## 5. `preview`

`[ ]` Render one node through one template, return the string, write nothing.

**Why.** The template authoring loop is currently generate → read a file off disk. This is the
tight one, and everything it needs already exists.

---

## Deliberately not decided

Two adjacent features cut against the "knows nothing about any target" purity that makes the
tool general. Both are adoption features rather than capability features, and whether they
belong is a product call:

- **Formatting hooks** — a per-template `gofmt` / `prettier` / `dotnet format` pass over the
  output. Generated code that does not match project convention is permanent diff noise. But
  it means shelling out arbitrary commands, which is a real security surface for an MCP
  server.
- **Starter template packs** — `init --preset csharp-entities`. Every new user currently
  begins at a blank Liquid file, which is the steepest part of adoption, but presets give the
  tool opinions about targets.
