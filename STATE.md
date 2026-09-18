# State

**Read this first when picking the project up again.** It is the authoritative record of
where the build is. Update it at the end of every working session, in the same commit as
the work.

**Version:** 1.0.0
**Last updated:** 2026-09-18
**Current block:** ten of ten have code in them. Nothing has been compiled and no environment
has been read, which is now the only thing standing between this and a first real run.

---

## Honest status

**Ten of ten blocks have code and none of it has met a compiler.**

The analysis engine is finished in the sense that every rule in the catalogue now has a
handler, the reference graph is built, the scorer computes the numbers, the estimator has
its three layers and the publisher has its gate. What it has never done is run.

| Layer | State |
|---|---|
| Contracts | 9, cross checked, consistent |
| Database | 3 migrations, 20 tables, never applied |
| Domain model | Complete |
| Offline extraction | Solution zip reader, 9 component families |
| Live extraction | Reader written. Flow run history deliberately not |
| Reference resolver | Complete |
| Rule handlers | **37 of 37** |
| Power Apps checker client | Complete |
| Scorer | Complete |
| Complexity rating and roadmap | Complete |
| Report model | Contract, plus the PDF and workbook renderers |
| Estimator | Complete, with guards and provenance |
| Backlog and DevOps publisher | Complete |
| Tests | 47, never run |
| Infrastructure | Ported from the migrator, never deployed |
| Pipeline orchestrator | Runner, 8 stages, worker loop, credential plumbing |
| Data layer | Stores written, schema never applied |
| Command line | `analyse` (with `--xlsx`, `--pdf`), `rules`, `components`. `work` not wired |
| API | Auth, access checks, 14 endpoints, all implemented |
| Web workspace | Shell ported, 6 workspaces, findings screen written |
| Microsite | **Not started** |

The machine this was written on had neither PowerShell nor the .NET SDK. **Nothing here has
been executed.** The C# has never seen a compiler, the SQL has never touched a database, the
PowerShell has never run and the tests have never been collected.

## What is checked, and how

Everything below was verified in Python against the contract files, not by running anything:

- 50 component types across 8 domains. Every domain, craft level, lifecycle state and
  evidence source resolves. 29 count toward the low code ratio. 10 are dated, deprecated or
  removed and every one carries a verification state.
- 37 rules. Every `appliesTo`, evidence source, estimate band and modernisation reference
  resolves, and every rule has a detection description.
- 35 of 35 actionable rules have an acceptance criterion with a given, a when, a then and a
  separate test requirement. There is no generic fallback: the backlog builder throws.
- 8 modernisation entries, each with a written case for leaving the component alone.
- 13 stages, exactly one of which writes to a target.
- 3 extraction modes, each stating its reach for all four evidence sources.

## The rules that keep this product honest

Six of them, and all six are enforced somewhere rather than written down:

1. **No point estimates.** No table anywhere has a column for one, and `Estimate.Create`
   refuses a low above a high.
2. **No estimate without a rationale.** Not nullable, checked for emptiness, at all three
   layers, in the database and in the constructor.
3. **A rule that could not run is not a rule that passed.** `inv.NotAssessed`, the `Reach`
   type, and a rule engine driven from the catalogue rather than from the handler list.
4. **Nothing writes to a Power Platform environment.** One stage in the contract declares a
   target write and a test asserts it is `publish`.
5. **A component nobody could measure is not a simple component.** `Complexity.Unrated` is its
   own level and its own series in the chart, because four hundred simple components plus forty
   unread ones is a different chart from four hundred and forty simple ones.
6. **The consulting half is not generated.** Functional maturity, ADKAR and PCT readiness are
   marked `written` in the report model and the contract check fails a written section with no
   prompt. A readiness score produced from metadata reads exactly like one produced from
   twenty interviews, which is precisely why it must not exist.

## Where the handlers stand

**37 of 37 written**, in three groups:

| Group | Count | Depends on |
|---|---|---|
| Offline | 16 | A solution file alone |
| Reference graph and metadata | 13 | The resolver, or a live connection |
| Runtime and checker | 8 | Run history, trace logs, or the checker service |

Sixteen of them run against `samples/SampleSolution.zip` today, which is what `docs/03-first-run.md`
is about.

## What is unproven, in order of how much it would cost to be wrong
## What is unproven, in order of how much it would cost to be wrong

0. **Nothing compiles yet.** Three bugs have been found by reading so far: a `??` chain that
   bound tighter than the `?:` after it, a custom `XmlException` shadowing
   `System.Xml.XmlException` so the read wrapper would never have caught a malformed solution
   file, and a Dockerfile guard checking for a generated file this product does not produce.
   All three are fixed. The compiler will find more, and the shadowing kind is the one worth
   watching for: it does not throw, it silently does nothing.



1. **The workflow category mapping decides what half the logic domain is, and it now exists in
   two places.** `SolutionZipReader` and `DataverseReader` each map the same category numbers,
   independently. If they ever disagree, an offline run and a live run of the same estate
   produce different reports and nothing says so. Pull it into one place the first time you
   touch either.** Everything in that domain arrives as a `Workflow` node and only the `Category` number
   tells a cloud flow from a dialog from a business rule. The mapping is from documentation.
   It does not throw when it is wrong: it classifies a business rule as a classic workflow and
   the report then tells a client to migrate forty things that are not there. **Check it
   against a real export before trusting any lifecycle finding.**

2. **The C# the generators emit has never compiled.** Warnings are errors in this repository.
   Expect the first run to fail on nullable annotations and on string escaping in the
   generated literals, which is where the migrator's generators bled most. Expect analyser
   complaints on the handlers that mix an argument null check with `yield return`, where the
   check is deferred until enumeration.
3. **The component to solution component type mapping is from memory and documentation, not
   from an environment.** The numeric `solutionComponentType` values on the component types
   are the ones most likely to be wrong, and they are wrong silently: an extractor asking for
   the wrong type code returns nothing, which looks identical to a solution with none of
   those components. Verify every one against a real environment before the extractor
   believes any of them. This is the same failure the migrator shipped and had to fix.
4. **Every lifecycle claim is an assertion about Microsoft's roadmap.** The ones marked
   `documented` were read on 2026-09-18. The ones marked `inferred` are a defensible reading
   and are not quotable as Microsoft's position. Classic workflows in particular are marked
   `dated` and **not** deprecated, deliberately, because calling them deprecated in a client
   report is wrong and gets caught.
5. **The detection descriptions are prose, not code.** Each rule says what triggers it in a
   sentence. Thirty-seven handlers have to be written and matched to those sentences, and the
   sentence is going to turn out to be ambiguous in at least a third of them.
7. **No estimate has been produced by anything.** The model estimation contract is written and
   no prompt exists. The guards in it, particularly the range sanity check and the band anchor,
   are guesses at what will go wrong rather than observations of what did.

## Decisions taken, and what they cost

| Decision | Why | What it costs |
|---|---|---|
| Model estimates rather than a calibration table | Peter's call. Scales to rules nobody has estimated before, and produces a rationale a person can read | A model will produce a confident number for a component it has not understood. Every guard in `estimate-model.json` exists for this |
| Per engagement override table beats the model | An estimate has to be defensible in a client room, and the first correction arrives ten minutes into the first conversation | Overrides have to survive a re-extraction, which means findings need a stable key before the extractor is written |
| Rationale required on every estimate, at every layer | It is what goes in the work item and what gets read aloud | An override with no rationale is rejected, which people will find annoying until the first time it saves them |
| Two axes, craft and lifecycle, not one | A plugin is not debt because it is code, and a classic workflow is not fine because it is low code | Every component type needs two judgements instead of one |
| Call the Power Apps checker, do not reimplement it | Microsoft maintains those rules and they move | A checker outage or timeout means half the analysis is unavailable, and the report has to say so loudly rather than look complete |
| Never write to a Power Platform environment | A discovery usable in a first meeting is worth more than any write | Remediation stays a recommendation. This product will never fix anything itself |
| Publish to DevOps behind an approval bound to a hash | Ported from the migrator, where the same gate exists for the same reason | An extra step everybody will ask to skip once |

## What is not built at all

- **Charts in the PDF.** Everything renders as a table today.
- **The consultant input screens** for the written report sections.
- **Delegated sign-in.** The interactive flow belongs to the web application, which now exists.
- **The microsite.** Not started.
- **Delegated sign-in.** Throws a `NotSupportedException` naming what it needs. The interactive
  flow belongs to the web application, so this unblocks itself when that exists.
- **The connect and selectSolutions stages.** Declared in the contract, not implemented. The
  connection test happens inside the extract stage today, which works and puts a failure one
  stage later than the contract says it should be.
- **Flow run history.** Still needs the Power Automate management API, a second token and
  separate consent. Eight rules report as not assessed until then, correctly.
- **Flow run history.** Needs the Power Automate management API, a second token and separate
  consent. Until then eight rules report as not assessed, correctly.
- **The API, the web workspace and the microsite.** All three port heavily from the migrator,
  which is why they are last rather than first.
- **The consultant input forms.** The written report sections need somewhere to type.
- **The consultant input forms.** The written sections need somewhere to type. That is a web
  screen per section plus a store, and it does not exist. Until then the report prints the
  prompt where the text should be.
- **Charts in the PDF.** Every visual in `report-model.json` renders as a table today. The
  components by customisation chart and the roadmap grid are the two worth drawing, and the
  Intent Miner's `ManagementReportBuilder.Charts.cs` is the thing to port for it.
- **The API and the web workspace.** Both port heavily from the migrator.

## The thing that most needs doing next

**Read `HANDOVER.md`, then compile it.** Everything else is guesswork until something has.

`Invoke-CodeGen.ps1` now checks the contracts, then runs the generators into a throwaway folder
and checks what they emit, then writes and builds. The first two gates should pass; the build
is where the work is.

One generator bug is already fixed this way, found by reading rather than running: all four
escaped backslashes wrong, which would have turned one backslash in a contract into four in the
C#. The migrator had it right.

Then `analyse samples/SampleSolution.zip`, then a real export.

Summarised:

1. `./build/Test-Contracts.ps1` — reads the contracts, connects to nothing.
2. `./build/Invoke-CodeGen.ps1` — generates and builds. Expect string escaping and nullable
   failures in the generated C#.
3. `dotnet test` — 30 tests.
4. `./build/New-SampleSolution.ps1` — a synthetic export with sixteen deliberate findings.
5. Run `SolutionZipReader` at it. A run finding fewer than sixteen tells you which reader is
   broken, because the script says what it planted.
6. Export one real solution from `powerpete.crm4.dynamics.com` and run the reader at that.
   This is the step that settles the category codes, the isolation codes, the web resource
   types and the connection reference element names, in an afternoon, with no application
   user and no security review.

Then, in order: the pipeline orchestrator, the data layer, the live Dataverse reader, the
API, the web workspace and the exports.

## What was ported rather than invented

| From | What |
|---|---|
| Migrator | `build/Common.psm1`, minus the topological sort, which has nothing to order here |
| Migrator | `Invoke-CodeGen.ps1`, `Directory.Build.props`, `.gitignore`, `.editorconfig`, `LICENSE.md` |
| Migrator | `locales.json` verbatim, with the export namespaces changed |
| Migrator | The stage and mode shape, the approval gate bound to a plan hash, and the read only until the last stage principle |
| Migrator | The engagement, system user and engagement access model, and the three role names. The suite uses the same three words deliberately |
| Migrator and Intent Miner | Contracts as the source of truth, generators, `STATE.md`, `VERSION`, semver, numbered documentation per language |
| Intent Miner | The run tier idea, as quick scan, assessment and full run |

Nothing was copied that did not fit. The canonical configuration model, the connectors, the
fidelity model and the apply ledger are all specific to a migration and have no equivalent here.
