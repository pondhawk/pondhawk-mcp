# Roadmap

Five things that would make pondhawk the strongest general code generation server, in the
order they should be built. Each entry records *why*, because the reasoning is the part that
gets lost.

Status: `[ ]` not started · `[~]` in progress · `[x]` done

**All five are done.** Below the line are the enforcement path and the adjacent features that
were considered and declined.

---

## 1. Dry run and drift — **done**

`[x]` **`generate(dryRun: true)`** — render to memory, diff against disk, write nothing.
`[x]` **`check`** — is the checked-in generated code what the model currently produces?

One engine, two tools. Render-to-memory plus compare-to-disk answers both questions; they
differ only in who is asking and what shape of answer they want.

**Why.** The write side is the least defended part of the pipeline: `validate_config` never
renders, `generate` renders and writes, and there is nothing in between. A dry run is how you
change one macro and see its effect across a fleet before committing to it, and it makes the
two quiet failures the guide warns about — a template that renders empty, a metadata key that
renders as nothing — *visible* rather than merely documented. `check` is the CI gate: it tells
an agent that just pulled a branch whether the generated tree is stale.

**Prerequisite — checked.** Output is byte-stable: the render path holds no timestamps, machine
paths or clock reads, and nodes are an ordered list. The one way to break it is a date filter in
a template, which is the author's choice rather than something to prevent.

**Constraint — met.** Both paths go through `GenerationPlanner` for rendering and path
resolution and `FileWriter.Decide` for the create/overwrite/skip decision, so a preview cannot
disagree with the run it previews. A test pins the prediction against the real run that follows
it.

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

## 5. `preview` — **done**

`[x]` Render one node through one template, return the string, write nothing.

**Why.** The template authoring loop was generate → read a file off disk: ceremony and leftover
artifacts for one answer.

**Built over the planner** rather than as a second rendering path — filtered to one template and
one node it does exactly this job, so a preview cannot drift from the run it previews. A test
pins its output against what `generate` then writes.

Errors return rather than throw, since a half-finished macro is the normal state while writing
one, and the three ways nothing renders — unknown node, an `AppliesTo` matching no Kind, an
`Ignore` override — are reported as three different messages rather than one empty result.

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

## The enforcement path

Three layers keep a project actually using the generator, and only the last one holds:

1. **Handshake instructions** answer "it was simpler to do it myself" — the reasoning error.
   Effective at connect time, decaying from there.
2. **`AGENTS.md` project preamble** answers "I forgot" — the salience problem. It lives in the
   repository, where the mistake gets made.
3. **`--check` in CI** answers neither and does not need to: it is the only layer that survives
   an agent having a bad day.

The third was advice a build script could not take until the binary grew a `--check` mode. It
only spoke MCP over stdio, so running the check meant writing a client. It now runs from a shell
and reports through an exit code — 0 clean, 1 not clean, 2 could not run, so a broken project
and a dirty one stay distinguishable.

## Adjacent features, both declined

Two features looked like natural extensions and cut against the "knows nothing about any
target" premise. Both are now decided:

- ~~**Formatting hooks**~~ — **decided: no**, and the trap they would have solved is documented
  instead.

  The problem is real, and sharper than diff noise. A formatter changes the bytes of a
  generated file, which is indistinguishable from a hand edit: `check` reports
  `EditedSinceGenerated`, the next `generate` reverts the formatting, the formatter reapplies
  it, and two tools own the file forever. The guide and README now say to keep formatters away
  from generated output and make the template emit conforming text, which `preview` makes cheap
  to iterate on.

  Four costs, and the first two are why:

  **It would be the first execution surface.** The tool runs no external commands at all today
  — Liquid rendering and file IO, no shell, no network. A command read from
  `pondhawk.project.json` is remote code execution triggered by pointing the server at a cloned
  repo, which is a routine thing to do with an MCP server. That is a categorical change to the
  threat model, not an incremental risk.

  **It would weaken determinism, which items 1 and 2 rest on.** Byte-stable output is what makes
  the manifest hash, `check` and dry-run diffs mean anything. A formatter is an unpinned
  external binary, so two developers on different `gofmt` or prettier versions get different
  hashes for the same model and templates, and `check` reports drift that is not there.
  "Deterministic given the model and templates" would become "and the local toolchain".

  **Only one shape works, and it excludes a major formatter.** It would have to pipe the
  rendered string through the formatter before writing: formatting after the write breaks
  atomic replace, breaks the manifest hash, and leaves the dry run unable to show what would
  land. But stdin/stdout rules out `dotnet format`, which operates on projects.

  **Failure semantics have no good answer.** A missing formatter either fails every run or is
  silently skipped; neither is right.

  **If it is ever revisited**, the command belongs on the server launch line beside `--project`,
  never in the repo config. That moves the trust boundary to the user's own MCP client
  configuration, where `--project` already comes from, and a cloned repo cannot inject it.

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
