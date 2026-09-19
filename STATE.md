# State

**Read this first when picking the project up again.** It is the authoritative record of
where the build is. Update it at the end of every working session, in the same commit as
the work.

**Version:** 1.0.0
**Last updated:** 2026-09-19
**Current block:** it is deployed and serving. A demonstration engagement seeds itself on
every start, the reports render with charts in six languages, the offline upload works end
to end, and a run queued through the API is picked up by the worker and produces findings
from a real export.

That last clause was true of a worker run on a laptop and false of the deployed one. The
deployed worker had never completed a single poll, from the first deployment to this one,
for three reasons at once. They are recorded below. The line stays because it is true again,
and this paragraph stays because the distinction between "the product does this" and "the
product did this once, locally" is the whole point of the file.

**What has never happened is a read of a live client environment.** Interactive sign-in has
never completed a round trip: it needs an account in a client tenant and a browser, and it
is the last unproven path of consequence. An earlier version of this line said interactive
sign-in works. It does not, and a state file that overstates the product is the one kind of
inaccuracy this repository cannot afford.

---

## Honest status

**It has met a compiler. The whole solution builds under warnings as errors and the tests pass.**

The analysis engine is finished in the sense that every rule in the catalogue now has a
handler, the reference graph is built, the scorer computes the numbers, the estimator has
its three layers and the publisher has its gate. All of that compiles under warnings as
errors and 121 tests exercise it.

It has read nine real exports from a live tenant, and each sweep of them found defects that
the synthetic sample could not: a component type nobody read, a read count that overstated
itself by a factor of twenty-eight, a rule firing on an estate that did not have the problem.
A real export remains the most productive test this product has.

`Api` and `Jobs` build too, now. They had been written against types that are not in this
repository, because the files were written at different times against different assumptions
about each other. What that cost is recorded under "what the API port left behind" below.

| Layer | State |
|---|---|
| Contracts | 9, cross checked, consistent |
| Database | 6 migrations, applied to Azure SQL |
| Domain model | Complete |
| Offline extraction | Solution zip reader, 9 component families |
| Live extraction | Reader written, including cloud flow run history |
| Reference resolver | Complete |
| Rule handlers | **37 of 37** |
| Power Apps checker client | Complete |
| Scorer | Complete |
| Complexity rating and roadmap | Complete |
| Report model | Contract, plus the PDF and workbook renderers |
| Estimator | Complete, with guards and provenance |
| Backlog and DevOps publisher | Complete |
| Tests | **97, all passing** |
| Infrastructure | Deployed to Sweden Central, with storage for uploads |
| Pipeline orchestrator | Runner, 8 stages, worker loop, credential plumbing |
| Data layer | Stores written, schema never applied |
| Command line | `analyse` (with `--xlsx`, `--pdf`), `rules`, `components`. `work` not wired |
| API | 14 endpoints, building. Three system health checks removed: see below |
| Web workspace | Shell ported, 6 workspaces, findings screen written |
| Microsite | Six languages, generated from the contracts |

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

0. **The engine compiles and 121 tests pass, so this list is shorter than it was.** What the
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

## Languages, and the public site

All five namespaces are complete in all six languages: **ui at 306 keys, backlog at 170,
finding at 111, inventory at 59 and report at 41.** That is 687 keys, 4,122 strings. Every rule's name, why it matters and recommended approach is
translated, keyed on the rule id rather than on its English text, so correcting a sentence in
the catalogue does not silently orphan five translations of it.

`Localiser` carries the English into every lookup. A key nobody has translated renders in
English, which is a document somebody can still hand over; a key nobody has written at all is a
compile error rather than a blank line on page four.

`analyse --language nl` renders both documents in Dutch, which is where a translation gets
checked before anybody sends it to a client.

**The translations are drafted, not natively reviewed.** They are consistent and the
terminology is deliberate, and none has been read by somebody who speaks the language.

The backlog is written in the engagement's `BacklogLanguage`, which is deliberately separate
from `ReportLanguage`: the team who picks the work up is not always the audience for the
report. Its work item titles use the same `finding` namespace the report does, so a finding
and the work item raised from it say the same thing.

### What the microsite is

`src/microsite`, built by `build.mjs` into the web root: English at `/` and every other
language in a folder of its own. Everything countable on the page is read from
`build/contracts` at build time, so the rule count, the component type count and the reach
matrix are the product's own numbers. A marketing page that keeps its own copy eventually
promises a client something the product does not do.

### Three defects this found, none findable by reading

- **The bundles resolve by walking up from the binary.** `AppContext.BaseDirectory` is
  `bin/Debug` on a developer's machine and `/app` in the container, so the first version wrote
  every document in English locally while working in production, which is the worst way round
  for a defect about languages.
- **Rule text was looked up in the `inventory` namespace rather than `finding`**, so the
  chrome translated and the content did not. The same slip happened again in the backlog
  builder, where the criteria were read from `finding` rather than `backlog`, and a test
  written for exactly that caught it.
- **Static files must run before routing.** `MapFallbackToFile` registers an endpoint matching
  the root, and once routing has selected an endpoint the static file middleware declines to
  serve. The microsite was in the image, reachable at `/index.html`, and invisible at `/`. The
  fallback's pattern excludes file-like paths, which is why the explicit paths worked and hid
  it. `app.UseRouting()` is now called explicitly, after the static files.

`check-vocabulary` checks every namespace against its own English, in both directions, and
refuses a translation whose placeholders do not match. Both shapes are checked: the interface
numbers its arguments and the backlog names them, and a criterion that loses `{componentName}`
in translation takes a component name out of a work item.

Two tests cover it: a backlog built in Dutch reads Dutch in its criterion, its title and its
headings, all three from different namespaces; and a language nothing has translated falls
back to English rather than rendering a key.

## The exports

Both documents are produced from any run that reached scoring:
`GET /api/engagements/{id}/reports` lists them and
`GET /api/engagements/{id}/reports/{file}` renders one.

**Composed on demand rather than produced by a run and kept.** A client's estate in a document
is the most sensitive thing this product makes, and a blob container quietly accumulating them
is a retention question nobody asked for. The run is stored, the document is rendered when
somebody asks, and it is identical every time because the score it quotes is the score the run
recorded rather than one recomputed.

`ReportComposer` lives in the API rather than in Export, because a renderer that needs a
connection string is a renderer nobody can test. It reads rows and returns the two models the
renderers already took.

### Three gaps closed to make the documents true

- **Nothing ever wrote `inv.Solution`**, though `GetFindingsAsync` had always joined it, so
  every finding and every component reported a null solution. For a product that analyses
  solutions that is the column that matters. The pipeline kept solution counts and threw the
  headers away; it keeps them now and writes them before the components, which resolve their
  solution by unique name.
- **The stored breakdown carried six fields of the score.** It carries the whole `RunScore` now.
  Runs written before this fall back to the totals on the score row and whatever breakdown they
  kept, because a report missing a chart is worth more than no report.
- **The command line leaves the component null on every finding**, which gives the workbook a
  column of blanks beside the column a consultant filters on first. Rebuilt from the database it
  is looked up by stable key, so the service's workbook is better than the command line's.

### Both renderers have run

Against the real IVR Toolkit export, for the first time in this repository's life:

| Sheet | Rows |
|---|---|
| Read this first | 18 |
| Findings | 14 |
| Backlog | 1, header only |
| Inventory | 345 |
| Customisation | 15 |
| Not assessed | 20 |

The PDF renders at 35 KB with no trial watermark. Without a licence key it still refuses to
start rather than producing a watermarked document somebody would be asked to sign. No API
drift in either ClosedXML or Syncfusion, which the handover had listed as likely.

### What is still unproven

**The database half, which is proven now.** This said the composer reading stored rows had
never run against real data. It has: a run queued through the API, picked up by the worker,
and read back by every screen. Finding out took fixing the defect that had stopped the
worker ever completing a job, which is recorded below.

**The solution fix only helps new runs.** Anything analysed before it has no solutions recorded
and will render with the column empty.

**The written sections have a screen now**, and the report no longer prints a prompt where
nobody has written one: it leaves the section out and says nothing about it, which is a
deliberate choice with a cost recorded below.

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
| Reports | A route that had never existed | Lists and downloads the workbook and the report |

`/api/extraction-modes` serves the contract rather than describing the modes a second time, so
the reach a screen promises is the same reach the report withdraws.

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

**All five namespaces are complete in six languages.** `backlog` was the last one empty and
is not any more, so work item titles and acceptance criteria are written in the language the
engagement asked for. Two tests hold the bundles to the keys the code actually uses, in both
directions.

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

### The read summary labelled a pass, not a type — fixed

`Reads:` printed `table 284` for a solution holding ten tables, because each label named the
read pass rather than what came out of it. The 284 was ten tables plus their columns, forms
and views. The components underneath were typed correctly and every rule keyed off those, so
nothing downstream was wrong; a consultant reading that list would still have drawn the wrong
conclusion, from a coverage section, in a product whose argument is that it does not overstate
what it read.

`Attempt` now counts the components a pass actually added, grouped by their type, and sums
where two passes produce the same one. A pass that produces nothing still records its own
label so an empty read is visible rather than absent.

The same conflation is in the command line's ratio line, which prints `5 % of 344 components`.
The denominator of the ratio is 22, not 344: configuration is counted and shown separately and
`RatioDefinition` says so. The sentence should say what the number is a share of.

## The second real export, and what a bundle can honestly be asked

`PresenceHub`, one PCF control, exported from the same environment. It came back reporting an
estate with **nothing in it**: no components, no failures, no findings. Nothing had ever read
`CustomControls`, and a solution that holds only code components therefore read as empty.

That is the most damaging thing this product can produce, so the guard is now structural. A
read producing zero components records a **failed** `solution` read saying the reader does not
understand this file, rather than letting ten honest zeroes sum into a clean bill. Model driven
apps and sitemaps are read now too, from `AppModule` and `AppModuleSiteMap`; reading the nested
`SiteMap` element found a node with no unique name on it and skipped every sitemap in the file,
and `Statecode` capitalised found nothing and made every app look stateless.

### What a minified bundle can be asked, and what it cannot

The control reads, and the harder question was what to say about the code in it. The answer
this product gives is deliberately narrow.

**Nothing here measures complexity from the bundle.** A cyclomatic count over webpack output
measures the bundler, not the developer, and a number that looks like an opinion about
somebody's code had better be one. Everything the developer named is now a single letter.

What survives minification is what the browser looks up by name at run time, and that is
exactly the set worth asking about: `setInterval` is a browser global, `innerHTML` is a DOM
property, `webAPI` is a property of an object the platform hands in. None of the three can be
renamed, so counting them is counting rather than inferring. Size is size, and minification is
a question about average line length.

Seven rules come out of that, in three kinds:

| Kind | Rules |
|---|---|
| A cost | `performance.pcfBundleSize`, `performance.pcfPollingTimer` |
| A question about the build | `quality.pcfNotMinified`, `quality.pcfDebugCodeShipped`, `modernisation.pcfBundlesOwnReact` |
| A defect | `quality.pcfUndeclaredWebApi`, and `security.pcfInnerHtml` as a surface rather than a hole |

Only the last kind asserts anything is wrong. A timer is reported as a question, because a
control whose purpose is elapsed time needs one; `innerHTML` is low, because telling a real
finding from constant markup takes the source and a bundle does not have it.

The tooling version is carried into the inventory and **not** judged against a current one. A
rule that bakes today's `pac` version in starts lying the month after it ships.

### The rules were right to stay silent, which is why nothing caught the real gap

`PresenceHub` is 63KB, minified, ships no React and carries no debug code, so the first four
rules correctly found nothing. What that exposed is that a well built control produced total
silence, which does not answer the question a client asks. The timer and `innerHTML` rules
exist because of that: the control starts four timers and writes markup in twenty-three
places, and those are the two sentences worth saying about it.

### Seven rules shipped untranslated in all six languages

They rendered their own resource keys. Nothing caught it, because every other guard here is
about the rule catalogue and the catalogue was complete — the bundles are hand written and the
catalogue is generated, so the two drift apart in exactly one direction.
`Every_rule_is_translated_into_every_language` now reads both and fails on the difference,
English included, since it is the fallback and has nothing to fall back to. It was confirmed
to fail on a removed key before being kept.

### The demonstration estate carries three controls now, not one

One built properly, one carrying a library it did not need, one shipped in a hurry. Between
them they trip all seven rules, and the first trips none of them, which matters more: a report
where every control has a finding against it reads as a tool that cannot tell good work from
bad. `DemoEstate.SeedVersion` is 3, so deployed demonstrations rebuild. It also carries 24
relationships and two publishers now, for the same reason: the offline reader produces them
from a real file, so a demonstration without them understates what the product does.

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
  page that a button could fix. **The demonstration engagement was wanted and was built**, as
  `DemoEstate` and `DemoSeeder`: it seeds itself on start, rebuilds when its seed version
  moves, and its findings are produced by running the real rule engine rather than written
  out, so it cannot disagree with the product.
- **`SystemHealth` is an instance, not a static class.** `Program.cs` called
  `SystemHealth.CheckAsync(connectionString, keyVaultUri, ct)`, which never existed; the class
  reads its own configuration and exposes `RunAsync`. It is registered in the container now.
- **`Secrets` never existed either.** `Secrets.cs` offers `ISecretStore` with a Key Vault
  implementation and one that refuses. Three call sites wanted a concrete `Secrets(uri)`, so
  `SecretStore.For(vaultUri)` now makes that choice in one place. Deciding it per call site
  would let the health page report a configured vault while the next save refused.
- **`UserAccess.Allows` is called `Holds`.** A rename, not a gap, and the access check is now
  bound to the method that ranks roles rather than to nothing.

### The web workspace and the API disagreed on routes, and do not now

Two of the three below are settled. They are kept because the shape of the mismatch is worth
remembering: none of it failed a build and all of it failed a screen.

- `SystemHealthPage.tsx` called `GET /api/system/health` and `Program.cs` mapped
  `GET /api/health/detail`. **Settled**: the API serves `/api/system/health`, in the shape
  the page reads, and the page renders.
- It also called `POST /api/system/health/{id}/repair`. **Settled by removal**: there is no
  repair endpoint and nothing for one to do, and the page no longer asks for one.
- It lists `sources` and `target` as check groups and nothing produces a check in either.
  The `SourcesGroup` and `TargetGroup` constants are still there, unused. **Still open**, and
  the smallest thing on this page: two constants, two words in the web workspace and two in
  `locales.json`.

## What is not built at all


### Built since, and what is left of each

- **Charts.** Nine of the eleven declared visuals are drawn: components by domain, the
  low code donut, components by customisation, lifecycle, findings by severity, where the
  hours sit, the estimate ranges, the roadmap grid and the maturity radar. The two that are not
  are componentsBySolution, which the score does not carry, and maturityVersusBenchmark,
  which needs a benchmark configured per engagement with its source and collection date. The
  ADKAR profile and the PCT triangle were dropped from the contract: both are change
  management instruments rather than anything an estate assessment produces, and the
  readiness section keeps its prose instead. Every chart sits beside the table it summarises, deliberately: a number that
  appears only in a picture is a number nobody can audit.
- **Interactive sign-in.** Works. Type the environment address, sign in, and the refresh
  token goes to Key Vault for the worker to redeem. Entra rotates refresh tokens and the
  worker stores back what it is given.
- **The demonstration engagement.** Seeds on startup against a stamped version, 369
  components and 99 findings, produced by running the real engine over a synthetic estate.
- **Flow run history.** Read from Dataverse's own flowrun table, which removed the need for
  the Power Automate management API, a second token and a separate consent entirely. The
  claim that run history was not in Dataverse had stopped being true. What is still not
  available anywhere in Dataverse is plug-in step execution timing, so
  performance.plugSyncSlow remains not assessed and says so.
- **The connect and selectSolutions stages.** Both implemented and in the pipeline in the
  order the contract declares. Connect authenticates every source without reading anything
  and records the identity; selectSolutions asks the environment what it has, which is what
  finally makes the analysed count and the total two different numbers.
- **The consultant input screens.** A Narrative workspace, generated from the report model
  so the prompt somebody answers on the screen is the prompt the document prints where they
  have not. Stored per engagement rather than per run, because a workshop is about the
  client and survives every re-extraction. Three of the five sections were declared in the
  contract and never emitted by the renderer at all; they are now.

## The thing that most needs doing next

**Sign in to a real environment interactively.** Everything else below the sign-in page has
now been exercised. The sample solution proves the readers
and the rules agree with a file this repository wrote; it cannot prove they agree with one
Dynamics wrote, and the category numbers, the isolation codes, the web resource types and
the connection reference element names all still come from documentation rather than from a
file somebody exported.

Two specific things nobody has yet checked against a real environment:

1. A plug-in assembly registered outside the sandbox, read from a genuine export. That is
   the last of the codes a file can settle.
2. The flowrun read. Its shape comes from documentation, and the reader refuses the whole
   read on a status it does not recognise rather than counting it as a success, so the
   failure mode is a rule reporting as not assessed. That is safe and it is not proof.

The build gates, for reference:

1. `./build/Test-Contracts.ps1` — reads the contracts, connects to nothing. Passes.
2. `./build/Test-Generators.ps1` — generates into a throwaway folder and checks the output.
   Passes.
3. `./build/Invoke-CodeGen.ps1` — generates and builds. Twelve of twelve, no warnings.
4. `dotnet test` — 121 tests, all passing.
5. `./build/New-SampleSolution.ps1` then `analyse samples/SampleSolution.zip` — sixteen
   planted defects, twelve found and four correctly not assessed.

## Nine real exports at once, and the third instance of the same defect

`PresenceHub` was not a one-off. Exporting nine solutions from `powerpete.crm4.dynamics.com`
and sweeping all of them together found the same defect twice more, and it is now clear it is
a *class* rather than three accidents.

The shape is always identical: a component type declared in the contract with `solutionZip`
evidence that no reader ever produces. It does not throw, does not warn, and does not reach
the not assessed list. The report simply has nothing where that type belongs, and reads as a
clean estate.

| Type | What was missing | How it was found |
|---|---|---|
| `pcfControl` | A solution holding one code component read as an estate with nothing in it | One export |
| `relationship` | 246 components across nine exports | The sweep |
| `serviceEndpoint` | One webhook to a Logic App | The sweep |
| `publisher` | One per solution, and the prefix every other judgement keys off | The sweep |

### The endpoint was worse than a hidden finding

`operability.noFailureAlerting` already checked `endpoints > 0` before firing. The handler was
written expecting a reader that did not exist, so from a solution file the count was always
zero and the rule fired on an estate that **does** have an endpoint. That was a High reported
against a client that was not true.

A missing reader does not only hide findings. It manufactures them, and the manufactured one
looks exactly like a real one.

`architecture.externalLogicInvisible` was also declared `metadata` only, although it needs
nothing a file cannot supply. It is `solutionZip` now and reports the webhook, with the host
alone: the full URL of a Logic App trigger carries its shared access signature in the query
string, and a report is a document that gets mailed around.

### Reading everything would have been worse than reading nothing

There are 32 `RibbonDiffXml` elements across the nine exports. Every single one is empty
scaffolding — `<CustomActions />` with no children — because the export writes it for each
table whether or not anybody touched the ribbon.

`commandBar` is pro code and counts toward the low code ratio, the most quoted number this
product produces. A reader that counted elements would have reported 32 pro code components
in an estate that has none. `ReadCommandBars` counts only ribbons with a `CustomAction` or a
`CommandDefinition` in them, so the type stays correctly absent here and appears the moment a
client actually has one. Wrong in the direction that looks like work is the worse failure.

### No rule reads the relationships yet, on purpose

246 relationships are now in the inventory and no rule looks at any of them, which is a
decision rather than an oversight.

The obvious rule is cascading delete, and 91 of the 246 are configured that way. But 90 of
those 91 are one table's fan-in: `SmsCenter` relates a single message table to every other
table in the estate, each with `CascadeDelete=Cascade`. Firing per relationship would produce
91 findings that are really one observation, and firing above a threshold means inventing a
threshold, which is how the other numeric codes in this reader went wrong.

A cascading delete is correct for genuine parent-child composition and wrong for a loose
reference, and a solution file does not say which one it is looking at. So the cascade
configuration is carried into the inventory where a consultant can sort on it, and nothing
claims to have judged it. That may be worth revisiting with row counts from a connected
environment, where "cascades onto fourteen million rows" is a fact rather than a guess.

### Eight types are still unread, and they are named now

`chart`, `dashboard`, `report`, `emailTemplate`, `customPage`, `customWorkflowActivity`,
`customConnector` and `copilotStudioAgent`. None of the nine exports contains one, so a reader
for them could only be written against documentation, and this repository has already been
wrong three times doing exactly that.

`Every_type_a_solution_file_can_carry_is_read_or_named` holds that list with a reason per
entry and fails in both directions: a newly declared type with no reader fails until somebody
writes one or records why not, and a type that gains a reader fails until it leaves the list.
That is the guard that was missing for all three of the defects above. It was confirmed to
fail on a removed entry before being kept.

## The report looks like its siblings now, and the microsite shows it

A client receives two of these from the same account team in the same month. A report that
does not look like the other one reads as a document from somebody else, so the furniture
matters more than it sounds.

The palette and the typeface were already right and both wordmarks were already embedded in
the assembly. **Nothing had ever drawn one.** The cover now carries the wordmark reversed out
of the navy band, the product name and "Make it real." opposite it, the blue rule under the
band, a prepared-by line, and for the demonstration estate an orange banner saying the data
is a sample. Every footer carries the engagement, the page number and the wordmark in blue.

### Two defects that only appear when you render it and look

**Syncfusion draws nothing when a line box does not fit its rectangle.** No exception, no
clipped glyph, an empty space where the text was. The default line height of 1.45 puts a 9pt
string a fraction of a point over a 14pt box, so the first render of the new cover came out
with the wordmark present and the product name and the tagline simply absent. This is the
second time this has cost an afternoon; the first was the donut centre.

**A running header is a document template, and a template paints over page content.** The
cover could not cover it, so a stray engagement name floated above the navy. The cover gets
its own section with `ApplyDocumentTopTemplate` switched off.

### The front page was three quarters empty

Band, four figures, then most of a page of white. It rendered, it was branded, and it read as
a document somebody abandoned. Page one now carries what the report says and the worst six
findings with their estimates, which is the shape the sibling reports use.

The consultant's paragraph where there is one, falling back to a generated sentence rather
than to the section prompt: a prompt is the right thing on the page that asks for it and the
wrong thing on a cover that gets forwarded, where it reads as an unfinished draft.

The hour range on the cover was formatted by plain interpolation rather than against
`Culture`, so it came out as `342,00-1.324 h` on a machine set to Dutch and would have
printed differently depending on which region the container happened to run in.

### The microsite

A report section with eight real pages from the demonstration estate, flipped with scroll
snap the way the siblings do it, and the hero gains the cover beside the headline. The hero
carried one column until now with a comment saying there was nothing true to put there and
that an invented picture of a product is worse than none. There is something true now.

Still no screenshots of the application itself. They need a signed-in session and the only
account on this machine is in the wrong tenant, so that is the one thing on the microsite
that is still missing.

## The report was English in six languages

The engagement has stored a report language since the first release and nothing ever read
it. Both documents defaulted to English, so a Dutch engagement produced a Dutch screen and
an English report, and all six translated bundles sat in the assembly unused. The composer
already loads the engagement; it passes the language now.

Rendering all six then found the quieter half of the same problem. **Fourteen keys the
renderer asks for had never existed in any bundle**, including three section headings, the
whole legend of the low code donut, and every severity value in every table.

Every lookup carries its English as a second argument, which is the thing that stops a
missing key leaving a hole on page four. It is also why this was invisible: a key that
exists in no bundle at all renders perfectly, in English, in all six languages, and nothing
ever says so. `Every_key_the_report_asks_for_exists_in_every_language` scans the renderer
for the keys it uses and holds every bundle to them, so a key added tomorrow is checked
tomorrow.

German runs a page longer than English and the cover's fitted table drops a row to make
room, which is the measuring doing its job. `Schweregrad` broke mid-word into
`SCHWEREGR / AD` until the severity column was widened.

### The consultant's sections are left out when nobody wrote one

They used to print with a heading and a bordered panel saying nobody had written it, on the
argument that a report missing its scenarios because nobody noticed is worse than one that
says so on the page.

That argument was about the wrong reader. A document that reaches a client with three empty
boxes in it and an instruction to the consultant inside each one does not read as candid, it
reads as a draft somebody sent by mistake, and the client cannot act on a prompt that is not
addressed to them.

A section nobody wrote is simply not in the document. A reader cannot tell the difference
between a section that was considered and left blank and one that was never part of this
report, and that is the accepted cost: the alternative put an instruction to a colleague in
front of a client. Functional maturity drops out entirely when nothing is scored and nothing
is written, which previously left a heading, a rule and an empty page.

An engagement with no consultant input is twelve pages rather than fifteen, with no empty
ones in any language.

## The offline upload works, end to end

Driven through the browser against the real storage account: a 24KB export picked in the
wizard, uploaded to blob, the connection saved with the blob name on it, and the connection
removed again afterwards through the two step Remove. The form has a real file picker, a
name, the environment it came from and the date it was exported, and the last two are there
because a file cannot say either and the report prints both.

Finding that out needed a role assignment, and the failure it produced first was worth
fixing. Everything in the upload path returns a sentence somebody can act on except the one
call that touches Azure, which threw straight out of the endpoint: a container the identity
cannot write to arrived on screen as a bare 500. The three that happen in practice are a
missing role assignment, a container that does not exist and a storage firewall, they need
three different people to fix, and they are told apart now.

## Interactive sign-in, as far as it can be taken without a tenant

The round trip still cannot be completed here. It needs an account in the client's tenant
and a browser, and the only account on this machine is in the wrong one. What could be
taken further is the half that decides whether to believe what comes back, and that is the
half worth testing: a callback that accepts a state it did not issue is a connection
somebody else can point at an environment of their choosing.

The state carries the connection's identity out through the browser and back. It is signed
and it expires, and neither had a test. There are five now, and the test project references
the API so that this logic is reachable from a test at all, which it was not.

Four of them are refusals, and refusals are easy to pass by accident: all three would have
passed against a `CompleteAsync` that returned null unconditionally. The fifth is the
control. A state the deployment signed gets past the check and the next thing it does is
read the connection, so with a store pointed at a server that is not there, a refusal
returns null and an acceptance fails reaching the database. That difference is the proof.

## Jira, beside Azure DevOps

A second target rather than a rewrite of the first. The two disagree about almost everything
at the wire and agree about the only two things that matter: an item has a parent, and an
item this product created before has to be found again rather than created twice. So the
rules are the same and only the calls differ, and both publishers refuse a backlog that has
changed since it was approved, refuse more than two hundred items without a confirmed count,
and never reopen anything somebody closed.

The deterministic key is a Jira label, the way it is an Azure DevOps tag. A label is the one
field present on every Jira project however somebody configured it, needs no custom field
created first, and is searchable with JQL.

Three things Jira does differently and neither the contract nor the publisher may assume
away:

- **Issue types are read from the project rather than named.** A team managed project and a
  company managed one do not offer the same ones and neither reliably has Epic, so each of
  ours falls down a chain: a feature that lands as a story is a board somebody can work
  with, and a publish that refuses because a project has no Feature type is not.
- **Descriptions are Atlassian Document Format, not HTML.** The backlog builder emits HTML
  because that is what the Azure DevOps field takes, and changing it would change what gets
  published there, so it is converted here. Only the shapes the builder emits are
  understood, which is all it has to understand: it is a converter for the six sections this
  product writes rather than a general one.
- **An API token is paired with the email it was issued to.** Neither works alone, so both
  are on the connection and the error says so, because "401" sends people to check the token.

### Tested against a recorder rather than a site

There is no Jira to point it at, so the round trip is not what is covered. What is, and is
the part most likely to be wrong, is the request it builds: thirteen tests assert what goes
on the wire through a fake handler.

That is not a formality. Every decision they cover is made before anything is sent and every
one fails in a way a smoke test would not catch. A missing label does not error; it creates
a second copy of the entire backlog the next time somebody publishes.

They found one immediately. The acceptance criteria are HTML and the test requirement is a
plain sentence, and the block converter is a regex over block elements, which finds nothing
in a plain sentence. So "How to prove it" was written as a heading and the text under it
silently was not: an empty heading on every issue this would ever have created. Untagged
text is now a paragraph.

The direction of a connection was an equality check against `azureDevOps` in three places.
It is one list now, which is what made adding the second target a contract entry and a
publisher rather than a hunt.

Opening the new form found the older gap it was hiding: `field.organisationUrl` had never
been translated and rendered as its own key, because nothing had ever opened the Azure
DevOps form either. The credential label said "Client secret" on every mode, which is right
for an app registration and wrong for both tokens, and the mode chooser told somebody
picking a target to pick the platform this connection reads.

## The worker had never run a job

Every run ever queued died before its first stage, and nothing noticed because nothing else
goes through that path. The demonstration estate is built by the seeder, the command line
analyser has no database, and the probes written during this work bypassed persistence. So
extraction, analysis, scoring, backlog building and storage had never once run together
against a real file through the queue.

`GetEngagementAsync` selected nine columns into an eight property record. Dapper could not
find a constructor for them and threw, and what it threw said "a parameterless default
constructor or one matching signature ... is required for Engagement materialization",
which names constructors rather than the column somebody added.

It runs now. An IVR toolkit export uploaded to blob, an offline connection pointing at it, a
run queued through the API and picked up by the worker: 403 components, 16 component types,
13 findings, 16 rules correctly reported as not assessed, 4.5 percent low code, status
partial. Those agree with what the offline probe finds in the same file, which is the point
of checking them.

### The guard that existed was the wrong half

`Never_selects_star_into_a_record` catches a starred select. This was an explicit column
list that had drifted from the record beside it, which fails identically and was not
covered. `Never_selects_a_different_number_of_columns_than_the_record_has` reflects each
record's constructor arity and counts the columns of the query that materialises it.

It took three attempts to make it real, and both failures are worth recording because both
are how a test ends up proving nothing:

- Searching forward from the call for a select found one belonging to a method further down
  the file, and reported an eight column record against a thirteen column query. A failure
  about nothing is worse than no test.
- Bounding that search at the next `Async<` found the one inside the call just matched,
  truncated the window to nothing, and passed cleanly against the exact defect it was
  written for.

Both directions are now checked by hand: with the bug reintroduced it fails naming the
count, and with it fixed it passes.

## The deployed worker had never run, and the sign-in could not find its vault

Reported as one symptom: interactive sign-in answered 503 with "No Key Vault is configured,
so there is nowhere to keep the sign-in." The vault existed, held a probe secret written by
this very deployment, and the operations page reported it healthy and writable.

Three separate defects, none of which produced an error anybody would read.

### One setting, four spellings

The bicep writes `KeyVault__Uri`. Three places read it, and no two agreed:

| Reader | Asked for | Found |
|---|---|---|
| System health | `KeyVault:Uri` | The vault. Wrote its probe secret, reported healthy |
| API startup | `KeyVaultUri` | Nothing. Built the secret store that refuses |
| Worker | `ANALYZER_KEYVAULT_URI` | Nothing |

So the page that exists to tell an operator whether the vault works said yes, and the code
that needs the vault behaved as though there were none. The same disagreement was live on
three more settings: the first global administrator and the support contact were written as
`Access__InitialGlobalAdminUpn` and `Support__AdminContact` and read as `InitialGlobalAdmin`
and `AdminContactEmail`.

The doc comment on `SecretStore.For` had already predicted this exactly, in those words, and
put the choice of store in one place so it could not happen. It was the right idea one layer
too low: the *name* of the setting is as much a part of the decision as the choice of store.
`DeploymentSettings` now names each setting once with its accepted aliases, and the API, the
worker and the health page all read through it.

### The worker's entry point named an assembly that has never existed

`PowerPete.Analyzer.Jobs.csproj` sets `AssemblyName` to `analyzer`, so its command line reads
like a tool. The Dockerfile wrote an entry point running `dotnet PowerPete.Analyzer.Jobs.dll`.
The container started, said the application did not exist, and restarted, every five minutes,
since the first deployment.

Nothing caught it because the API half of the same image ran perfectly, the version numbers
agreed, and nobody reads the log of a component whose only job is to be quiet.

### And then it printed its help and exited successfully

With the assembly found, the container ran the dispatcher with no arguments, because the
bicep set `command` and no `args`. The dispatcher's answer to no arguments is its help screen
and exit 0. The platform reads exit 0 as a container that finished, and starts it again.

A crash loop that exits successfully and prints a help screen is invisible twice over: there
is no error, and the log looks like documentation.

### What holds each of them now

- `Reads_every_setting_the_infrastructure_writes` parses the container definition and fails in
  both directions. A name the infrastructure writes and no reader accepts is an operator
  setting something correctly and watching the feature stay off. A setting the code needs and
  the infrastructure never writes is a feature off in every deployment.
- `Runs_the_worker_assembly_the_build_actually_produces` reads `AssemblyName` out of the
  project file and holds the Dockerfile's entry point to it. The image also asserts the file
  exists at build time, so a rename fails a build rather than a deployment.
- `Tells_the_worker_container_to_work` holds both the bicep argument and the image's fallback
  to `work` when it is given none.

All three were checked by reintroducing the defect: each fails naming the real cause, and
passes once fixed.

### What this cost, and the pattern

Every run ever queued through the deployed product sat in the queue. The estate on screen was
built by the seeder, the reports were rendered by the command line, and every probe of the
pipeline bypassed the deployment, so nothing in the product ever asked the worker for
anything and noticed it was not there.

This is the same shape as the column arity defect two sessions ago: the failure was total,
permanent and silent, and it survived because the thing that would have revealed it was the
one path nobody had run end to end. The lesson is not about settings or about Docker. It is
that a component with no output is a component with no evidence, and this product now has
three tests whose whole job is to be that evidence.

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
