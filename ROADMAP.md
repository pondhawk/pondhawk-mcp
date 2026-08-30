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

## 2. A manifest — **done**

`[x]` Record what was written: path, template key, source node, content hash
(`.pondhawk/manifest.json`).

**Why.** Small, and it makes three currently-impossible things possible:

- **Orphan cleanup.** Delete a node from the model today and its generated file lives on disk
  forever. A manifest lets the tool report — and safely remove — files it produced and no
  longer produces. Safe because it only ever touches files it recorded, whose hash still
  matches; never a `SkipExisting` stub, never a hand-edited file.
- **Hand-edit detection.** An `Always` file whose hash has drifted means someone edited
  generated output and is about to lose that work. There is nowhere for that warning to come
  from today.
- **Write avoidance.** Done, and it needed no manifest: item 1's content comparison already
  says when a file is already correct, so `generate` leaves it untouched rather than rewriting
  it identically and waking every watcher downstream. Skipping the *render* too would need
  input hashing, and rendering was never the expensive part.

## 3. Shared macros — **done**

`[x]` A top-level `Partials` list, composed into every template's source before parsing.

**Correction.** This entry originally proposed a Fluid `FileProvider` and `{% include %}`. That
does not work, and it was worth finding out by experiment rather than by shipping: dispatch
resolves a macro by name in the template context at render time, and Fluid renders an include in
a *child* scope, so the macro is created and discarded before dispatch looks for it. Measured
side by side, an included macro yields
`/* dispatch error: macro 'DefaultProperty' not found */` while the same macro concatenated
ahead of the template renders correctly. `{% include %}` is deliberately still not enabled — it
would look like a second way to share macros and quietly fail.

**Why.** This is the gap that undermines the core promise. "Every node of a Kind goes through
one macro, so changing that macro changes every artifact at once" holds *within* a template
file. A project with entity, DTO, validator and mapper templates duplicates `DefaultProperty`
four times, and the moment those drift the fleet stops being uniform in exactly the way this
tool exists to prevent. `TemplateOptions` sets no `FileProvider` today, so `{% include %}` and
`{% render %}` are not wired up at all.

## 4. `describe_model` — **done**

`[x]` Report the Kind vocabulary, metadata keys observed per Kind with counts, node counts and
tree depth.

**Why.** The guide tells an agent to reuse the Kinds already in use, and not to introduce
`DataType` beside an existing `Type` — then gives it no way to see either without reading the
whole model. On a large model that is expensive and easy to skim. This turns a stated
obligation into something checkable.

**Built with notices**, which is what separates it from a dump: it flags a sparse key beside a
similarly-named dominant one, near-duplicate and plural/singular Kinds, one key holding two
value types, and Kinds differing only in case — the last because `AppliesTo` matches
case-insensitively while dispatch builds the macro name from the literal Kind, so `Class` and
`class` silently resolve to different macros.

**Considered and rejected:** cross-referencing Kinds against declared `Default<Kind>` macros to
flag a Kind nothing can render. Since item 3 macros are per template plus partials, so that is a
per-template question, and a single global answer would be confidently wrong for any project
with more than one template — and would also false-alarm on Kinds rendered directly by a
template body rather than through dispatch.

## 5. `preview`

`[ ]` Render one node through one template, return the string, write nothing.

**Why.** The template authoring loop is currently generate → read a file off disk. This is the
tight one, and everything it needs already exists.

---

## Decided along the way

**Run-level transactions: no.** A transactional file manager was considered and rejected. It
solves a problem this tool does not have and misses the one it does. Rolling a failed run back
would mean deleting files that generated correctly, contradicting the deliberate per-file
failure design — and generated code is not a database: generation is deterministic and
idempotent, so recovery is "fix the error and run again", with `check` and the manifest saying
what state the tree is in meanwhile.

**Per-file atomicity: yes.** Writes go through a temporary file in the same directory and are
renamed into place, so a crash leaves the old file or the new one and never a mixture. Rendering
already completes to a string before any write begins, so this closed the last route by which a
partial file could reach disk. The manifest already wrote this way; the generated files did not,
which was backwards.

## Deliberately not decided

Two adjacent features cut against the "knows nothing about any target" purity that makes the
tool general. Both are adoption features rather than capability features, and whether they
belong is a product call:

- **Formatting hooks** — a per-template `gofmt` / `prettier` / `dotnet format` pass over the
  output. Generated code that does not match project convention is permanent diff noise. But
  it means shelling out arbitrary commands, which is a real security surface for an MCP
  server.
- ~~**Starter template packs**~~ — **decided: no**, and `init` was corrected to match.

  An LLM writes Liquid for today's target, informed by this project's own conventions, which
  `describe_model` and `Partials` exist to expose. A shipped pack writes whatever was idiomatic
  when the binary was compiled, knows nothing about the project, and carries a confident label
  saying otherwise. It would freeze target conventions — file-scoped namespaces, records,
  nullable reference types — against a binary that versions independently of the language, and
  drift into output that compiles and looks plausible, which is the failure mode the rest of
  this work exists to eliminate.

  A `language` parameter is worse, not lighter: N sets of frozen conventions instead of one, an
  obligation to cover the next language asked for, and the contradiction written into the tool
  signature directly beneath "knows nothing about any particular target".

  `init` was itself a starter pack under another name — it scaffolded `public partial class`,
  `namespace`, `{ get; set; }` and `.cs`, a holdover from the database-schema focus. It now
  scaffolds Markdown with `Section`/`Field` Kinds. The neutrality is the mechanism, not a
  compromise: a C#-shaped example invites editing into production, whereas an example in a
  format nobody ships invites replacement, which is what keeps one example from becoming a
  pack.
