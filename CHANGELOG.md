# Changelog

Every release, newest first. Semver: a breaking change to a contract is a major, a new
contract or a new rule is a minor, everything else is a patch.

## 1.0.0 — 2026-09-18

Findings read back. Handover written. Nothing has been compiled.

### Added

- The read side of `AnalysisStore`: findings joined to their estimates and components in one
  query, the not-assessed list, the stored score, the backlog, the latest scored run, and
  setting and removing an engagement override.
- `GET /findings`, `POST /overrides` and `GET /backlog` now do the work instead of returning
  501.
- `HANDOVER.md`. What exists, the first session in order, the three things to check before
  trusting any output, the four enforced rules, the deliberate refusals, and what to build
  next.

### Behaviour worth knowing

- **The findings query joins the component with a LEFT join.** A solution wide finding has no
  component, and an inner join would silently drop exactly the findings that outrank everything
  else in the report.
- **`GET /findings` defaults to the latest run that reached scoring**, not the latest that was
  started. A run that died during extraction would otherwise show an empty estate and look like
  a finished one.
- **With no completed run it returns "no run", never an empty findings list**, and carries a
  caveat saying this is an unread estate rather than a clean one. Different sentence, different
  conclusion.
- **Setting an override replaces the one at the same scope** rather than adding a second. Two
  competing overrides would resolve by whichever the query returned first, which is a number
  nobody could explain and which could change between runs.
- **Rule text is joined on at read time** rather than stored per finding. It is the same
  sentence for every finding of a rule by design, and storing four hundred copies would mean a
  corrected sentence only fixing the ones written afterwards.

## 1.0.0-alpha — 2026-09-18

Every block has code in it. Nothing has been compiled.

### Added

- `build/Test-Generators.ps1`. Runs every generator into a throwaway folder and checks the C#
  it emitted: unbalanced braces, a string literal not closed on its own line, an unterminated
  verbatim string. Not a compiler, and it catches the class of bug that costs an afternoon,
  which is a contract string containing a character the quoting did not handle. Wired into
  `Invoke-CodeGen.ps1` before anything is written to the repository.
- `PowerPete.Analyzer.Api`. Entra sign-in, cookie session, one access check every engagement
  endpoint goes through, engagements, connections, runs, approval, findings, overrides and
  health.
- The web workspace, ported from the migrator: the shell, the access model, i18n, theme,
  access and user administration, documentation and support. Six workspaces replacing the
  migrator's eight: Overview, Connections, Runs, Findings, Backlog, Reports.
- `FindingsPage`. The screen a consultant spends the engagement in. Caveats above the list,
  evidence and the false positive note in the detail, and an override form whose rationale is
  required by the form because it is required by the database and by the report.

### Fixed

- **All four generators escaped backslashes wrong.** PowerShell's `-replace` treats only `$` as
  special in the replacement, so the pattern `'\\'` with replacement `'\\\\'` turns one
  backslash into four rather than two. No contract contains a backslash today, so it was
  latent; the first one added would have produced C# that does not compile, in a generated file
  nobody wrote. The migrator had it right and I did not copy it carefully enough.

### Deliberately returning 501

Two endpoints exist and refuse rather than returning an empty result:

- `GET /findings` — `AnalysisStore` writes findings and nothing reads them back yet. An empty
  list would render as a clean estate on the one screen where that conclusion gets made.
- `POST /overrides` — validates the request, then says the write is not implemented.

Both fail loudly on purpose. A 501 is a morning's work; a screen quietly showing no findings is
a report somebody forwards to a client.

## 0.9.0 — 2026-09-18

The worker runs a real pipeline. Still nothing compiled.

### Added

- `AzureOpenAiModel`, the first `IEstimateModel`. One finding per call, temperature zero, JSON
  response format asked for rather than parsed out of prose. Every failure returns null rather
  than throwing, because the caller already holds a band default and one throttled call should
  not lose four hundred findings.
- `ConnectionFactory`. Tokens for Dataverse, the Power Apps checker and Azure DevOps, personal
  access tokens included, plus reading an uploaded solution file from private blob storage with
  a managed identity.
- `StageServicesFactory`. Every argument is a closure over one connection, so a stage never
  sees a credential and never picks one.
- `Worker.ExecuteAsync` now builds the stages, the fatal-stage map and the runner, and runs a
  real pipeline. `approve` and `cancel` return without doing anything: both are recorded by the
  API against a person, and a worker that could approve on somebody's behalf would make the
  gate meaningless.
- `Generate-AcceptanceCriteria.ps1`, a fifth generator, and `AcceptanceCriteria.Load()`. The
  criteria travel inside the assembly, so a deployment cannot separate the code from the text
  it publishes.
- `WorkspaceStore.GetRunAsync`.

### Fixed

- Five duplicate `PackageVersion` entries I introduced while adding the blob and OpenAI
  packages. Central package management refuses to build with a package declared twice, so this
  would have failed on the first `dotnet build` rather than doing anything subtle.

### Behaviour worth knowing

- **The checker geography is never defaulted.** Where a client's solution is uploaded for
  analysis is a data residency decision. With none set the checker stage reports itself as
  unavailable and three rules go unassessed.
- **The connection test is recorded before anything is read.** A service principal that
  authenticates and holds no role reads an environment with nothing in it, and that row is how
  somebody notices before a client does.
- **`ReachedRuntime` is hard-coded false.** Run history is not in the Dataverse Web API, so
  asking for it is not the same as reaching it, and the rules needing it stay unassessed.
- Delegated sign-in throws a `NotSupportedException` naming what it needs: the interactive flow
  belongs to a web application that does not exist yet.

## 0.8.0 — 2026-09-18

Contracts reach the runtime. The worker starts. Still nothing compiled.

### Added

- `Generate-EstimateModel.ps1`, a fourth generator: the estimate bands, the per engagement
  fixed costs, the story point scale and the complexity rules, all from the contracts.
- `Generate-RuleCatalogue.ps1` now emits each rule's roadmap position, plus a
  `RuleCatalogue.Roadmap` lookup.
- `ComplexityRule.FromContract()` and `RoadmapBuilder.FromContract()`. One conversion at one
  boundary rather than parsing in two places.
- `WorkerSettings`, `StoreJournal`, `StorePersistence` and `Worker`. Configuration read from
  environment variables and checked before the loop starts, migrations applied on start, a
  single threaded claim-run-record-sleep loop, and a worker that survives one bad command.
- `analyzer work` now starts, applies the schema and polls. It throws a named
  `NotImplementedException` on the one thing still missing rather than pretending.
- Five tests holding the generated catalogues to the contracts that produced them.

### Fixed

- **The command line carried its own copy of the band table.** Five literals that also lived in
  `estimate-model.json`, free to drift, and drifting would have produced two different numbers
  for the same estate with nothing saying which was right. It now reads the generated
  catalogue, and a test compares the two.
- The command line passed an empty roadmap and rated every component `Unrated` in its exports.
  Both are now built, so `--xlsx` and `--pdf` from a local file carry the customisation chart
  and the roadmap.

### Still missing

- `StageServices` needs blob storage for the uploaded solution file and tokens for the
  environment, the checker and Azure DevOps. Everything around that plumbing exists: the
  journal, the persistence, the runner, the stages and the readers. `ExecuteAsync` throws with
  that sentence rather than silently doing nothing.

## 0.7.0 — 2026-09-18

The deliverables. Still nothing compiled.

### Added

- `PowerPete.Analyzer.Export`, ported from the migrator: `CapgeminiBrand`, the embedded Ubuntu
  typeface and wordmark, and the `PdfSurface` / `Flow` / `Table` helpers.
- `FindingsWorkbook`. Six sheets, and the order is the point: **Read this first** comes before
  the findings, because somebody who reads a short findings list without knowing a third of the
  checks never ran will conclude the estate is clean. Findings carry their evidence in the row,
  not just in the report. The inventory's unrated complexity cells carry a comment saying they
  are not the same as simple.
- `AssessmentReportPdf`. Follows `report-model.json` section by section. The caveats are page
  two, before any number. Written sections print their prompt and a note saying nothing
  generated them, rather than being dropped: a report missing its scenarios because nobody
  noticed is worse than one that says so on the page.
- `analyse` now takes `--xlsx <file>` and `--pdf <file>`. A failure writing either does not
  fail the command; the analysis already printed and is what somebody ran it for.

### Notes

- Syncfusion rather than QuestPDF, ported unchanged. QuestPDF's Community licence is free only
  below one million dollars of annual revenue and does not cover commercial Capgemini work.
  Without a key Syncfusion does not fail, it stamps a trial banner on every page, so the
  renderer refuses to start rather than letting a watermarked document reach a client.
- The CLI's PDF and workbook currently pass no roadmap and no per-component complexity: the
  offline command does not build either yet. Both render as empty sections rather than as
  invented ones.

## 0.6.0 — 2026-09-18

The offline path is runnable from a command line. Still nothing compiled.

### Added

- `PowerPete.Analyzer.Pipeline.Stages`. Eight stage implementations: extract, checker, resolve,
  analyse, estimate, score, backlog and publish. `StageServices` is passed in rather than
  resolved from a container, so a stage can be exercised in a test with a fake for one thing
  and nothing at all for the rest.
- `PowerPete.Analyzer.Jobs`, an executable named `analyzer`. Four commands:
  - `analyse <solution.zip>` runs the whole offline path against a file. No database, no
    environment, no credentials, no configuration. Prints the reads, the findings, the score
    and the not assessed list.
  - `rules` and `components` print the catalogues.
  - `work` polls the command queue, and currently says it is not wired up.
- `AnalysisStore`. Inventory merged on the stable key so a replayed normalisation updates
  rather than doubling every count, findings written with their estimates in one transaction,
  not-assessed records, the score with its breakdown, the backlog and the publish record.
- Handlers are found by reflection rather than listed. A hand written list is one somebody
  forgets to add to, and a missing handler does not fail: its rule reports as not implemented
  and the category looks smaller than it is.

### Behaviour worth knowing

- **The extract stage fails above a quarter.** Below that a run carries on and the report names
  what it missed. Above it the report would be describing an environment nobody read, and a
  plausible document is worse than no document.
- **Two sources merge rather than overwrite.** A zip carries a flow's definition and a live
  read carries its owner and run history. Taking whichever arrived second would lose half of
  whichever it was, so the richer attribute wins per field.
- **Estimation checkpoints per finding.** A resumed run does not pay a second time for the same
  model calls.
- **The publish stage refuses without a matching approval**, and says why in a sentence rather
  than returning an error code.
- `analyzer work` fails with an explanation instead of looping silently. A worker that does
  nothing and looks healthy on a dashboard is worse than one that will not start.

## 0.5.0 — 2026-09-18

The engine can now, on paper, run end to end. Still nothing compiled.

### Added

- `PowerPete.Analyzer.Pipeline`. `RunState`, `IStage`, `StageOutcome` and `PipelineRunner`.
  Three things make it more than a for loop: a stage that already succeeded is skipped on a
  resume, whether a failure is fatal comes from the contract rather than from the runner, and
  a run where anything came back partial ends as partial rather than succeeded.
- `BacklogHash`. Computed from what a person read on the screen, in a fixed order. Reordering
  the backlog changes nothing; changing a title, an estimate or a criterion changes everything.
- `PowerPete.Analyzer.Data`. `AccessStore`, `DatabaseMigrator` and `Secrets` ported from the
  migrator, plus `WorkspaceStore`: engagements, connections with credential expiry, runs,
  stage checkpoints, the worker command queue claimed by an update with a predicate, and the
  approval bound to a backlog hash.
- `PowerPete.Analyzer.Dataverse.DataverseReader`. Solutions, custom tables, every process
  category, plugin assemblies, step registrations, roles with their assignment counts,
  connection references, environment variables, service endpoints and reports. Pages by
  following the next link rather than trusting a count, and stops on an empty page.
- Six pipeline tests, taking the suite to 42.

### Deliberately not implemented

- Flow run history. It is not in the Dataverse Web API: it needs the Power Automate management
  API, a separate token and separate consent. `ReadFlowRunStatisticsAsync` throws with that
  sentence rather than returning zeros, because zeros would make every flow look like it never
  fails.
- The table a plugin step is registered against. It lives on a filter record rather than on the
  step and needs a second call per step. Left for a later pass rather than guessed.

### Notes

- Role assignment counts return null rather than zero when the count could not be read. Zero
  means nobody holds the role; null means nobody could find out, and the sprawl handler refuses
  to fire on the second.
- No method in the Dataverse reader writes. There is no create, update or delete in the file
  and no code path that puts one there.

## 0.4.0 — 2026-09-18

Report model. Taken from a CRM assessment Capgemini already delivers by hand, with the line
drawn between what a tool can produce and what only a consultant can write.

### Added

- `build/contracts/report-model.json`. Fifteen sections, each marked generated, written or
  hybrid. A written section ships a prompt and no numbers; a hybrid one says which half is
  which. Thirteen visuals declared, including the two the assessment format is built on.
- **Complexity as a third axis** on `component-model.json`. Craft says what a component is
  made of and lifecycle says how much life it has left; neither says how hard this instance is
  to move. Ten measured rules, and `unrated` is a level rather than a rounding down to simple.
- `ComplexityRater` and the components by customisation chart: counts per component category
  split simple, medium, complex and unrated.
- **Roadmap placement** on all 37 rules: two axes, three bands. `RoadmapBuilder` turns findings
  into the grid, one box per rule rather than per finding, and prints the band distribution.
- Six more tests, taking the suite to 36.

### Changed

- The contract checker now enforces roadmap placement on every rule, complexity levels and
  measures, report section kinds, and that no benchmark ships in the box.

### Fixed

- The `view` component type never declared `linkedEntityCount`, which the zip reader has been
  writing since 0.2.0. Every view would have come back unrated, and an unrated column looks
  exactly like an extraction that did not reach far enough.

### Deliberately not built

- Functional maturity scoring, ADKAR and PCT readiness. The product holds the scores and draws
  the charts and generates none of the numbers. A readiness score produced from metadata reads
  exactly like one produced from interviews, which is what makes it dangerous.
- Any industry benchmark. It is the most quotable figure in a report of this kind and the
  easiest to invent. A benchmark configured per engagement carries its source and date, and
  both print on the chart.

## 0.3.0 — 2026-09-18

Third block, and the handover point. Every rule now has a handler, and the build tooling,
tests and infrastructure are in place. Still nothing compiled.

### Added

- `ReferenceResolver`. Builds the graph ten rules depend on: which view shows which column,
  which workflow filters on what, which flow uses which connection reference, which workflow
  calls which custom activity. A reference pointing outside the inventory is recorded rather
  than dropped, because "this column is unused" and "this column is used by something we did
  not look at" are different answers.
- `Scorer`. The low code ratio with its definition carried as data, counts per craft, domain
  and lifecycle, debt per domain, and the caveats that go at the top of a report rather than
  in a footnote: unassessed rules, a partial solution set, band-only estimates, low confidence
  clusters and flagged estimates.
- 21 more rule handlers, taking the catalogue to **37 of 37**. Reference graph, metadata,
  runtime and checker-backed. `CheckerBackedHandler` groups what the checker returned under a
  rule in this catalogue so the finding gets an estimate, a criterion and a backlog place,
  while the detection stays with Microsoft.
- `CheckerClient`. Upload, analyse, poll, download, SARIF parsing and the category and
  severity mapping. Never throws on a checker failure: three rules go unassessed and the
  report says so, rather than looking complete.
- `AnalysisContext` now carries checker issues and the environment count, so the checker
  rules and the environment separation rule have evidence to read.
- 30 tests across contracts and engine. The contract tests read the JSON from disk rather
  than the generated code, so they catch a contract edited without regenerating.
- `build/New-SampleSolution.ps1`. Generates a synthetic solution export with sixteen
  deliberate findings, listed in the script. The whole offline path runs on a laptop with no
  client file, no environment and nobody's permission, and a run producing fewer than sixteen
  says which reader is broken.
- Infrastructure, Dockerfile and deployment scripts ported from the migrator: Bicep for
  Container Apps, Key Vault, SQL and monitoring, plus `Deploy-Infrastructure.ps1`,
  `Initialize-Database.ps1`, `Grant-DatabaseAccess.ps1` and `Publish-Container.ps1`.
- `docs/03-first-run.md`. Six steps for the first session in Visual Studio, with the two
  that are expected to fail marked as such and the likely failures named.

### Fixed

- The Dockerfile's generated-code guard checked for a file the migrator produces and this
  product does not, so every container build would have failed on a correct repository.

### Known gaps

- Nothing has been compiled or executed. See `STATE.md`.
- No live Dataverse reader, so 21 handlers have no evidence to work from yet.
- No pipeline orchestrator, no data layer, no API, no web application, no exports.

## 0.2.0 — 2026-09-18

Second block. Database schema, domain model, the offline extractor and the first half of the
engine. Still nothing compiled.

### Added

- `db/migrations/0001_schemas_and_ops.sql`. Four schemas. Engagements, system users,
  engagement access with the suite's three role names, connections carrying no secret, runs,
  the approval bound to a backlog hash, run stages with checkpoints and the worker command
  queue. Two constraints do real work: only a publish run may hold a target connection, and a
  publish must derive from an existing assessment rather than re-analysing.
- `db/migrations/0100_inventory.sql`. Solutions, components, the reference graph, unresolved
  references, and the two tables that keep this product honest: `stg.EntityRead`, whose
  constraint forces a successful read to carry a count and a failed one to carry a reason, and
  `inv.NotAssessed`, one row per rule that could not run.
- `db/migrations/0200_findings.sql`. Findings with their evidence, estimates, model call
  provenance, the engagement override table, the run score and the backlog. Ranges only and
  rationale required are both constraints rather than conventions, at all three estimate
  layers.
- `build/contracts/acceptance-criteria.json`. All 35 actionable rules, each with a given, a
  when, a then and a separate test requirement. The block one gap is closed, and
  `Test-Contracts.ps1` now fails on a missing criterion rather than warning.
- `PowerPete.Analyzer.Domain`: `DiscoveredComponent`, `StableKeys`, `Finding`, `NotAssessed`,
  `Reach`, `AnalysisContext`, `Estimate` and the band table. The stable key is built from the
  platform identifier and never from a display name, so a rename cannot orphan an estimate a
  workshop agreed.
- `PowerPete.Analyzer.Extraction.SolutionZipReader`. Reads an exported solution: entities,
  columns, forms with their script libraries, views, workflows of every category, web
  resources, connection references, environment variables, roles, plugin assemblies, canvas
  apps and global choices. Cloud flow definitions are walked recursively for action count and
  nesting depth, because a flow with one scope holding forty actions is exactly the one the
  size rule is looking for.
- `PowerPete.Analyzer.Analysis`: the rule engine and 16 handlers covering everything a
  solution file alone can reach. The engine is driven from the catalogue, so the 21 rules
  without a handler report as not assessed rather than disappearing.
- `PowerPete.Analyzer.Estimation`: the three layer estimator, the prompt with the band passed
  in as an anchor, the rejection and flagging guards, and the provenance record.
- `PowerPete.Analyzer.DevOps`: the backlog builder with its grouping, the six fixed
  description sections, placeholder substitution that drops a sentence rather than publishing
  a brace, and the publisher with its approval hash check, dry run and count confirmation.

### Fixed

- A `??` chain in the backlog builder that bound tighter than the `?:` following it. Found by
  reading, not by a compiler, because there is no compiler yet.
- A custom `XmlException` in the extraction namespace that shadowed `System.Xml.XmlException`.
  The read wrapper would never have caught a malformed solution file, and would have failed
  the whole run instead of recording one unreadable entity.

### Known gaps

- Nothing has been compiled or executed. See `STATE.md`.
- 21 of 37 rule handlers are unimplemented, each waiting on the checker client, runtime
  evidence, the resolve stage or a live connection.
- The workflow category mapping decides what half the logic domain is and has never been
  checked against a real export.

## 0.1.0 — 2026-09-18

First block. Repository, contracts and the contract to code loop.

### Added

- Repository skeleton ported from `d365-contactcenter-migrator`: build props, package
  management, editor config, gitignore, licence and solution file.
- `build/contracts/component-model.json`. 50 component types across 8 domains, classified on
  two independent axes: craft, which drives the low code to high code ratio, and lifecycle,
  which drives the technical debt list. Every lifecycle claim that is not `current` carries a
  verification state and, where it is documented, a source and the date somebody read it.
- `build/contracts/rule-catalogue.json`. 37 rules across 9 categories, each naming its
  evidence sources, its estimate band and, where one exists, its false positive case. The
  Power Apps checker is called rather than reimplemented and its results are mapped onto the
  same categories.
- `build/contracts/modernisation-map.json`. 8 entries, each offering options with what they
  gain and what they cost, and each carrying a written case for leaving the component alone.
- `build/contracts/estimate-model.json`. Ranges only, three layers of precedence, the model
  estimation contract with its guards and recorded provenance, and the per engagement override
  table. Rationale required at every layer.
- `build/contracts/analysis-stages.json`. 13 stages and 4 run modes. Exactly one stage writes
  outside the product's own database, and it sits behind an approval bound to a backlog hash.
- `build/contracts/extraction-sources.json`. Three connection modes with an honest reach
  matrix, and the rule that an unreachable evidence source produces a not assessed record
  rather than a pass.
- `build/contracts/devops-mapping.json`. Work item grouping, field mapping, the description
  section order, the deterministic key that makes a publish idempotent, and the dry run and
  count confirmation safety rails.
- `build/contracts/locales.json`, ported. Six languages.
- `build/Common.psm1`, ported from the migrator without its topological sort.
- `build/Test-Contracts.ps1`. Cross references every contract before a generator runs, because
  a broken reference does not fail a build, it fails at run time as a rule that silently
  matches nothing.
- `build/Invoke-CodeGen.ps1` and three generators: component model, rule catalogue, locales.
- `src/PowerPete.Analyzer.Domain` with the English UI resource bundle.

### Known gaps

- Nothing has been compiled or executed. See `STATE.md`.
- 32 of 35 actionable rules have no acceptance criteria written.
- `solutionComponentType` values are from documentation, not from an environment.
