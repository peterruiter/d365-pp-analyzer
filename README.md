# Power Platform Solution Analyzer

Reads a Power Platform or Dynamics 365 estate and says what is in it, how much of it is
technical debt, what the low code to high code ratio actually is, and what it would cost to
put right.

Produces a report a client reads, a workbook a consultant filters, and a backlog that can be
published into Azure DevOps, Jira or GitHub as work items with acceptance criteria, estimates and
story points.

The third product in the suite, beside
[Intent Miner](https://github.com/peterruiter/d365-contactcenter-intentminer), which answers
what customers are calling about, and
[Contact Center Migrator](https://github.com/peterruiter/d365-contactcenter-migrator), which
answers what it would take to move. This one answers what you already have and what it is
costing you to keep.

## How it reads an environment

| Mode | Needs | Reaches | Use it when |
|---|---|---|---|
| Application user, read only | An app registration and a read only role | Everything except some runtime evidence | The engagement is repeatable |
| Interactive sign in | The consultant's own account | Everything | A workshop, where somebody is watching |
| Exported solution file | Nothing | Everything static, nothing operational | Week one, while the access request sits in a queue |

Modes combine. What one cannot see, the report says it could not see, per rule, by name.
Nothing is ever reported as passing because nobody could read it.

## What it does not do

**It never writes to a Power Platform environment.** Not in any mode, and there is no flag
that changes it. A discovery is safe to run in a first conversation, which is the point.

**It does not reimplement the Power Apps checker.** Microsoft maintains those rules and they
move. This product calls the checker and folds the results in under their own ids. The rules
declared here are the ones the checker does not have: lifecycle, cross component debt, usage,
sprawl and solution hygiene.

The only thing it writes anywhere is a backlog, into Azure DevOps, Jira or GitHub, and only the
items somebody picked on the backlog screen. A dry run reports what would land without writing
anything, and every item carries a deterministic key, so publishing twice updates what is there
rather than creating a second copy.

## Run modes

| Mode | Writes | Runs the checker | Produces |
|---|---|---|---|
| Quick scan | Nothing | No | Counts, the ratio, lifecycle findings. Fifteen minutes |
| Assessment | Nothing | Yes | The full finding set, estimates, report and backlog |
| Compare | Nothing | Yes | This run against a previous one |
| Publish | Azure DevOps, Jira or GitHub | No | Work items, from a chosen backlog |

## Estimates

Every estimate is a range with a rationale. Three layers, in order of precedence:

1. A per engagement override, set by a consultant, which beats everything.
2. A model estimate, one finding at a time, with its prompt version and payload hash recorded.
3. The band the rule declares.

No point estimates anywhere. The database has nowhere to put one.

## Picking this up

`HANDOVER.md` first. It says what has never been run, what to check before trusting any output,
and what the deliberate refusals are so you do not "fix" one by accident.

## Getting started

Full walkthrough in `docs/03-first-run.md`. In short:

```
./build/Test-Contracts.ps1
./build/Invoke-CodeGen.ps1
dotnet test
./build/New-SampleSolution.ps1
```

The last one writes a synthetic solution export with sixteen deliberate findings in it, so
the whole offline path runs with no client file, no environment and nobody's permission.

## Where things are

| Path | What |
|---|---|
| `build/contracts` | The source of truth. Change behaviour here, not in code |
| `build/generators` | Contract to code |
| `build/Test-Contracts.ps1` | Cross references every contract before a generator runs |
| `src/PowerPete.Analyzer.Domain` | The model. No dependencies |
| `src/PowerPete.Analyzer.Extraction` | Reading an exported solution file |
| `src/PowerPete.Analyzer.Analysis` | The rules, the reference graph and the score |
| `src/PowerPete.Analyzer.Estimation` | Three layers, ranges only, rationale always |
| `src/PowerPete.Analyzer.DevOps` | The backlog and the publisher |
| `db/migrations` | The schema. Two contract rules are enforced here as constraints |
| `docs` | Written for a consultant who has never touched Azure |

`HANDOVER.md` is where to start. `STATE.md` says where the build actually is, including what is unproven.

## Licence

MIT. See `LICENSE.md`.

The .NET packages, npm packages and Azure services this project depends on are licensed
under their own terms, which the MIT licence does not change. The PDF report uses
Syncfusion, which needs its own licence key. See `Directory.Packages.props` and
`src/web/package.json` for the dependency list.

Nothing in this repository contains client data. Any deployment that processes client data
does so inside a boundary agreed with that client, under a separate processing agreement,
with a retention period and a scripted deletion path.
