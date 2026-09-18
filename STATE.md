# State

**Read this first when picking the project up again.** It is the authoritative record of
where the build is. Update it at the end of every working session, in the same commit as
the work.

**Version:** 1.0.0
**Last updated:** 2026-09-18
**Current block:** it is deployed and serving. The image builds, the API answers, the web
workspace is wired to it in six languages, and the migrator is out of the screens. Nobody has
signed in yet, so everything past the sign-in page is built and untested against a real session.

---

## Honest status

**It has met a compiler. The whole solution builds under warnings as errors and the tests pass.**

The analysis engine is finished in the sense that every rule in the catalogue now has a
handler, the reference graph is built, the scorer computes the numbers, the estimator has
its three layers and the publisher has its gate. All of that now compiles under warnings as
errors, 56 tests exercise it, and it has read a solution file and produced findings, a score
and a set of estimates. What it has still never read is a real export.

`Api` and `Jobs` build too, now. They had been written against types that are not in this
repository, because the files were written at different times against different assumptions
about each other. What that cost is recorded under "what the API port left behind" below.

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
| Tests | **56, all passing** |
| Infrastructure | Ported from the migrator, never deployed |
| Pipeline orchestrator | Runner, 8 stages, worker loop, credential plumbing |
| Data layer | Stores written, schema never applied |
| Command line | `analyse` (with `--xlsx`, `--pdf`), `rules`, `components`. `work` not wired |
| API | 14 endpoints, building. Three system health checks removed: see below |
| Web workspace | Shell ported, 6 workspaces, findings screen written |
| Microsite | **Not started** |

The machine this was written on had neither PowerShell nor the .NET SDK. That is no longer
true: the contracts check, the generators run, the engine builds and the tests pass. The SQL
has still never touched a database and no environment has been read.

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

0. **The engine compiles and 56 tests pass, so this list is shorter than it was.** What the
   compiler found is worth recording, because it says what kind of bug is still in here.

   Not one error came from generated code. The generators were right, which means the Python
   emulation that checked them was a fair substitute for running them. Everything that failed
   was hand written.

   Three were the quiet kind. The `Severity` enum was shadowed by a `Severity(string)` method
   on the same class, which is the `XmlException` failure a second time and in the same file
   that had already worked around it once on its last line. An unnamed fallback tuple erased
   the element names from a conditional, so `metadata.Category` did not exist. And `{{` is not
   an escape inside a raw interpolated string, so the estimator's JSON response schema was
   being read as an interpolation hole.

   Two were in PowerShell and stopped everything before it started: `ConvertTo-PascalCase` had
   no body at all, so `Common.psm1` never closed and nothing in `build/` could run, and the
   contract check read an optional `visuals` property under `Set-StrictMode`.

   The rest were the analyser under warnings as errors, and none of them changed behaviour.

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

## The first run against the sample solution

`New-SampleSolution.ps1` plants sixteen defects and names them, so the run has an expected
answer rather than a plausible one. It now produces all sixteen:

- **Twelve fire.** Dialog, both cloud flow rules, the hard coded endpoint, the environment
  variable default, the form script weight, the show-and-hide script, the API key, the two
  publisher prefixes, the logic spread, the orphaned columns and the missing descriptions.
- **Four report as not assessed, correctly.** Both classic workflows, the plugin isolation and
  the organisation level write need metadata or runtime evidence, and a file carries neither.
  They are named, with what they needed, and none is reported as passing.

Three more fire that the script does not plant: no failure alerting, view complexity and the
low code ratio. All three are solution wide and read as fair.

### The workflow category mapping is right, against this file

The unknown at the top of this list for months. All six planted workflows resolve correctly:
category 0 mode 0 to a background workflow, category 0 mode 1 to a real time one, 1 to a
dialog, 2 to a business rule and 5 to a cloud flow, twice. That is exactly what the sample
plants.

**This is not the same as being right against Microsoft.** The sample's category numbers were
written from the same documentation the reader maps, so this proves the two halves agree with
each other. A real export is still the only thing that settles it.

### Two rules were matching nothing, silently

Both found by running this file rather than by reading, and both are the failure this product
exists to avoid: a rule that cannot match anything looks exactly like an estate with nothing
wrong.

- **`alm.hardCodedEnvironmentValue` and `security.secretInDefinition` never saw a flow.** Both
  read a `definitionText` attribute and the reader never wrote one; it counted the actions and
  threw the text away. Both rules were searching web resources only and reporting every cloud
  flow in every estate as clean. The reader stores the definition now.
- **`alm.publisherPrefixSprawl` could not fire at all.** A column's schema name arrives as
  `table.column`, so taking the prefix from the front of the string reported every column
  under its table's prefix. One prefix, every time, in every solution. It reads the last
  segment now.

The first of the two is the more expensive: it was wrong on every estate rather than on some
of them, and a clean flow is exactly what a client wants to hear.

## Taking the migrator out of the screens

Six screens were the Contact Center Migrator's. They were not broken analyzer screens: they were
built on canonical entities, target fidelity and apply plans with creates and conflicts, and no
amount of wiring would have made them read an estate.

The web called 29 endpoints and the API implemented 13. **All 30 resolve now**, most of them
thin wrappers over stores that already had the query.

| Screen | What it was built on | What it reads now |
|---|---|---|
| Connections | Which of several platforms to read, from a connector contract | The four extraction modes, from `extraction-sources.json`, which the contract always said it generated |
| Overview | An assessment of entities and a plan of record changes | The analyzer's score, and a checklist worked out from what exists rather than stored |
| Backlog | Grouped by canonical entity, with a reason and evidence | Work items by type, in priority order, with the acceptance criterion and the test requirement |
| Runs | A declared level per canonical entity | What each component type gave up, per evidence source |
| People, access, health | Routes that had never existed | Wired |
| Reports | A route that had never existed | **Still cannot produce a document.** See below |

`/api/extraction-modes` serves the contract rather than describing the modes a second time, so
the reach a screen promises is the same reach the report withdraws.

### Reports are the one thing wiring could not fix

There is no export stage in the pipeline, nowhere to keep a document, and no way to read the
component inventory back out of the database. **This deployment cannot produce a workbook or a
report however many analyses somebody runs.** Only the command line can, with `--xlsx` and
`--pdf`.

The endpoint says which of the two reasons applies and the screen says the matching sentence,
rather than sending somebody to run another analysis for a document that will never arrive.
Building it needs an export stage, somewhere to put the output, and a component read-back.

### Also removed

- The system health repair button, which existed to reseed the demonstration engagement that was
  never built. Applying migrations and rotating credentials are things a person does as
  themselves, with a record of having done it.
- `MIGRATOR_` environment variables, which the C# had already stopped using, the
  `contactcenter-analyzer` image repository, the web package name, `levelTag`, `fidelityTag`, and
  a support link that mailed about the wrong product.

## The first deployment

`rg-ppanalyzer` in **Sweden Central**, subscription PowerPete MVP. Entra application
`PowerPete Analyzer`, app id `747be66d-dec8-4e67-a41b-e6f252ba1e0c`, secret expiring
2027-09-18, redirect URI on the deployed API.

Up and serving: SQL server and database, Key Vault, container registry, container apps
environment, the API and worker apps, Log Analytics and Application Insights. The API is at
`https://ppanalyzer-api.calmforest-a31e153d.swedencentral.azurecontainerapps.io`.

`/api/version` reports 37 rules and 50 component types, `/api/auth/status` answers anonymously,
every language bundle serves, and the protected endpoints redirect to sign in rather than
answering.

**The container app probes `/health/live` and `/health/ready` and the API mapped only
`/healthz`.** Both returned 404, the liveness probe failed three times at thirty second intervals
and the platform killed the container: a restart roughly every seventy seconds, with the
application logging a successful start every time round. That is why the logs looked healthy
while nothing answered. Neither probe consults the database, deliberately: Azure SQL serverless
pauses itself, and a probe that restarts the thing it is measuring turns a slow dependency into
an outage.

**`az containerapp logs show` does not work from the Capgemini network.** The proxy intercepts
TLS with a self-signed certificate and the log stream endpoint refuses it. Use
`az monitor log-analytics query` against the `ppanalyzer-logs` workspace instead.

**West Europe refused to create a SQL server at all** (`RegionDoesNotAllowProvisioning`). That
is capacity, not the template. Sweden Central took it unchanged.

### What the deployment found

- **`Initialize-Database.ps1` called a command that did not exist.** `dotnet run --
  migrate-database` was the documented way to build the schema and the command line had no
  such verb, so the schema had never been buildable by the documented route. Added.
- **The template's `sqlConnectionString` output carried no `Authentication` clause**, so the
  string the script prints for a person to paste fails the login on an Entra only server with
  18456, which reads like a missing permission. The container app was never affected:
  `main.bicep` appends the clause for it, and only the human facing output was wrong.
- **The Dockerfile built a microsite that is not in this repository.** `COPY src/microsite`
  failed every build before it reached the compiler.
- **`npm ci` had no lock file.** Committed one.

### What the database rejected

The schema had never met a database. It failed three times, and none of it was findable by
reading:

- **`CONNECTION` is reserved inside a `REFERENCES` clause.** `REFERENCES ops.Connection
  (ConnectionId)` is a syntax error while the same name parses fine in a `SELECT` or a
  `CREATE TABLE`, so the table could be created and then not be referenced. Bracketed.
- **Two tables cascaded from two parents that both cascade from the run.** SQL Server refuses
  outright, so `inv.UnresolvedReference` and `findings.BacklogItemFinding` could not be created
  at all. Both cascade from one parent now, which is what every other table in the schema
  already did.

Applied: 24 tables in four schemas and 44 check constraints, including both constraints on
`stg.EntityRead` and the rationale checks. `CK_EntityRead_Evidence` reads
`Succeeded = 1 AND RecordCount IS NOT NULL OR Succeeded = 0 AND FailureReason IS NOT NULL`,
which is rule three of the six enforced in the database rather than written down.

### What blocked the image, and does not any more

`check-vocabulary.mjs` reported 221 problems: 202 string keys used by the pages with no entry in
`ui.en.json`, and nineteen CSS classes with no rule. `i18n.tsx` resolves a missing key as the key
itself, so those screens rendered `people.admit` where a label belongs.

All six languages now carry **301 keys each**, checked for coverage and for placeholder parity:
no bundle gains, loses or reorders a `{0}`. The findings screen is styled, including the severity
pills, which is the one thing on that page a consultant reads at a glance.

**The translations are drafted, not natively reviewed.** They are consistent and the terminology
is deliberate, and none of them has been read by somebody who speaks the language.

**Only the `ui` namespace has bundles, in any language including English.** `report`, `finding`,
`backlog` and `inventory` are declared in `locales.json` and empty, and the report a client reads
comes from `report` and `finding`. The interface is six languages; the deliverables are not.

The web application fetches every language but English from `/api/locales/{code}/{ns}` and that
endpoint did not exist, so the bundles could never have reached a browser. English is imported at
build time and the client keeps English on a failed fetch, so the failure was silent.

### Not built, found while deploying

`Initialize-Database.ps1` finishes by recommending `./build/Test-DatabaseRoundTrip.ps1`, which
does not exist. The constraints above are verified to be present and not yet proven to fire.

## The first run against a real export

`PowerPeteIvrToolkitCore`, exported from `powerpete.crm4.dynamics.com` on 2026-09-18. 344
components. It is the run the handover said to do in an afternoon, and it earned its place.

### What it settled

- **Connection reference element names are right.** `connectionreference` with
  `connectionreferencelogicalname`, one in the solution, one read.
- **The workflow category mapping held.** One classic background workflow, read as one.
- **Web resource types and the plugin assembly read correctly**, though with one of each there
  is not much to be wrong about. The isolation codes are still unverified: that needs an
  assembly that is not sandboxed, and this solution's is.

### What it found, and both were invisible rather than wrong

- **Environment variables: fourteen in the solution, none read.** A real export writes one
  file per variable under `environmentvariabledefinitions/`, and `customizations.xml` carries
  nothing. The reader looked only at `customizations.xml`. The synthetic sample puts them
  inline, which is why the rule fired there and on nothing real. Both places are read now.
- **Custom APIs: nineteen in the solution, no reader at all.** `customApi` is a declared
  component type with `solutionZip` in its evidence, and nothing had ever read one. They did
  not appear in the findings and did not appear in the list of things that could not be read,
  which is the worse half: a declared type that is silently absent rather than reported as
  unreadable.

**The second one moved the headline number by a factor of seven.** Custom APIs are pro code
and count toward the ratio. Before the fix this solution reported as 33 percent low code, on
one low code component and two pro code ones. It is 5 percent: one and twenty-one. A ratio is
the most quoted figure this product produces and it was wrong by that much because one reader
did not exist.

### Still not settled

- **Plugin isolation codes.** Needs an assembly outside the sandbox.
- **Step registrations and dataflows.** Correctly reported as unreadable from a file.
- **Everything the checker, metadata and runtime carry.** 17 of 37 rules could not run, named,
  with what each needed.

### The read summary labels a pass, not a type

`Reads:` prints `table 284` and `classicWorkflowBackground 1` because each label names the read
pass rather than what came out of it. The 284 is ten tables plus their columns, forms and
views, and the workflow pass covers dialogs, business rules and cloud flows too. The components
underneath are typed correctly and every rule keys off those, so nothing downstream is wrong.
A consultant reading that list would still draw the wrong conclusion from it.

The same conflation is in the command line's ratio line, which prints `5 % of 344 components`.
The denominator of the ratio is 22, not 344: configuration is counted and shown separately and
`RatioDefinition` says so. The sentence should say what the number is a share of.

## What the API port left behind

Making `Api` and `Jobs` build meant deleting things rather than writing them, because what
they referenced was never here. Each of these is reversible and each is a decision:

- **The connector and target health checks are gone.** `Connectors`, `ConnectorReaders` and
  `TargetContract` read `ConnectorCatalogue`, `TargetCatalogue` and
  `PowerPete.Analyzer.Connectors.ConnectorFactory`. All three are migrator concepts. A
  discovery product has no connectors and no target mapping, so the checks had nothing to
  check. `Api.csproj` also referenced the `PowerPete.Analyzer.Connectors` project, which is
  not in this repository at all.
- **The demonstration engagement is gone with them, and it was never built.** `DemoAsync` and
  the repair beside it needed `DemoSeeder`, `DemoEngagement` and an `ops.DemoSeed` table. The
  table is not in any migration, the seeder was never written and the constants do not exist.
  `RepairAsync` existed only to reseed it, so it went too, and with it the only thing on this
  page that a button could fix. **If the demonstration engagement is wanted, it is a feature
  to build, not a check to restore.**
- **`SystemHealth` is an instance, not a static class.** `Program.cs` called
  `SystemHealth.CheckAsync(connectionString, keyVaultUri, ct)`, which never existed; the class
  reads its own configuration and exposes `RunAsync`. It is registered in the container now.
- **`Secrets` never existed either.** `Secrets.cs` offers `ISecretStore` with a Key Vault
  implementation and one that refuses. Three call sites wanted a concrete `Secrets(uri)`, so
  `SecretStore.For(vaultUri)` now makes that choice in one place. Deciding it per call site
  would let the health page report a configured vault while the next save refused.
- **`UserAccess.Allows` is called `Holds`.** A rename, not a gap, and the access check is now
  bound to the method that ranks roles rather than to nothing.

### The web workspace and the API do not agree on routes

Found while doing the above and **not changed**, because it is a runtime mismatch rather than
a compile error and the right side to move is a decision:

- `SystemHealthPage.tsx` calls `GET /api/system/health`. `Program.cs` maps
  `GET /api/health/detail`.
- It also calls `POST /api/system/health/{id}/repair`. There is no repair endpoint, and after
  the demonstration engagement went there is nothing for one to do.
- It lists `sources` and `target` as check groups. Nothing produces a check in either group
  any more. The `SourcesGroup` and `TargetGroup` constants are still there, unused, because
  the same two words are still in the web workspace and in `locales.json`.

None of this fails a build and all of it fails a screen.

## What is not built at all

- **Charts in the PDF.** Every visual in `report-model.json` renders as a table today. The
  components by customisation chart and the roadmap grid are the two worth drawing, and the
  Intent Miner's `ManagementReportBuilder.Charts.cs` is the thing to port for it.
- **The consultant input screens.** The written report sections need somewhere to type: a web
  screen per section plus a store. Until they exist the report prints the prompt where the
  text should be, which is correct and not finished.
- **Delegated sign-in.** Throws a `NotSupportedException` naming what it needs. The interactive
  flow belongs to the web application, so this unblocks itself when that exists.
- **The connect and selectSolutions stages.** Declared in the contract, not implemented. The
  connection test happens inside the extract stage today, which works and puts a failure one
  stage later than the contract says it should be.
- **Flow run history.** Needs the Power Automate management API, a second token and separate
  consent. Until then eight rules report as not assessed, correctly.
- **The demonstration engagement.** No `ops.DemoSeed` table, no seeder, no constants. Until it
  exists, everybody admitted sees an empty product on their first sign in.
- **The microsite.** Not started.

## The thing that most needs doing next

**Run it against a real export.** The sample solution proves the readers and the rules agree
with a file this repository wrote. It cannot prove they agree with one Dynamics wrote, and the
category numbers, the isolation codes, the web resource types and the connection reference
element names all still come from documentation.

In this order:

1. Export a solution carrying a plugin assembly outside the sandbox and run the reader at it.
   That is the last of the codes a file can settle.
2. Apply the migrations to a real Azure SQL instance and prove the constraints fire,
   particularly the two on `stg.EntityRead` and the rationale checks.
3. Decide which side moves on the API and web route mismatch below. It fails a screen today.

The build gates, for reference:

1. `./build/Test-Contracts.ps1` — reads the contracts, connects to nothing. Passes.
2. `./build/Test-Generators.ps1` — generates into a throwaway folder and checks the output.
   Passes.
3. `./build/Invoke-CodeGen.ps1` — generates and builds. Twelve of twelve, no warnings.
4. `dotnet test` — 56 tests, all passing.
5. `./build/New-SampleSolution.ps1` then `analyse samples/SampleSolution.zip` — sixteen
   planted defects, twelve found and four correctly not assessed.

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
