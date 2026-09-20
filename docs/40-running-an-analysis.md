# Running an analysis

## The four modes

| Mode | Does | Takes |
|---|---|---|
| **Quick scan** | Reads the estate and applies every rule, using estimate bands rather than individual estimates. | Minutes. |
| **Assessment** | A quick scan, plus an estimate per finding and a groomed backlog. | Longer, and it calls a language model. |
| **Compare** | An assessment measured against an earlier run, so you can show what changed. | As an assessment. |
| **Publish** | Writes the chosen backlog items into Azure DevOps. Reads nothing and re-analyses nothing. | Minutes. |

Start with a quick scan. It is the mode that is safe to run in a sales conversation, and it
answers most of the questions a first meeting has.

## Choosing what to read

A run does not read everything it can find, and it does not decide for you.

Within seconds of starting, it connects, lists every solution in the environment, and then
**stops and asks**. You get the list, with each solution's publisher, version and component
count, and you tick the ones the report should cover.

Microsoft's own solutions start unticked. Most of what a Dataverse environment contains was
put there by Microsoft, reading it is the longest part of a run, and the report it produces
is about Dynamics rather than about the work your client paid somebody to do. Nothing is
hidden: every solution found is on the list and recorded whether or not you tick it, because
a report covering four of nineteen solutions and one covering all nineteen look identical on
the cover page.

Four checks can be turned off on the same screen:

| Check | Costs | If you turn it off |
|---|---|---|
| **Export the chosen solutions** | Around a minute per solution, so a dozen is a quarter of an hour. Nothing is written: an export is a read. | The fourteen rules that read a solution file, and the three that need the checker, are reported as not assessed. A live connection is then no richer than a metadata read. |
| **Solution checker** | The slowest part of a run by a wide margin. | Every rule whose evidence is a checker result is reported as not assessed, never as passing. |
| **Model estimates** | Minutes, and a model endpoint. | Estimates fall back to band defaults, which is what a quick scan does. The report says which it used. |
| **Environment health** | Seconds. | The run proceeds on whatever the credential happens to reach. |

Ticking nothing is allowed and produces a report that says this is an unread estate rather
than a clean one. The screen warns you before you continue.

## The stages

A run moves through ten stages, and the run screen shows where it is, how long each took and
which one it is on now:

1. **Check connections** — authenticates and reports the identity it authenticated as.
2. **Select solutions** — lists what the environment holds, then waits for you.
3. **Extract** — reads the solutions you chose.
4. **Checker** — submits the solution to the Power Apps checker and waits.
5. **Resolve** — joins components to each other, so the rules can ask what points at what.
6. **Analyse** — applies every rule in the catalogue.
7. **Estimate** — puts a range of hours on each finding. Skipped by a quick scan.
8. **Score** — computes the ratio, the customisation chart and the roadmap.
9. **Backlog** — turns findings into work items somebody would actually groom.
10. **Publish** — writes to Azure DevOps or Jira. Only in publish mode, and only the items somebody chose.

The stage that is running says what it is doing while it does it — which solution it is
exporting, which one the checker has, how far through the environment it is — and the screen
updates on its own. A stage that reads a client's estate takes minutes, and without that a
slow one and a stopped one look exactly the same.

A stage can be run again on its own, from the run screen. Doing so also discards every stage
after it, which the button tells you before it does it: a run whose findings came from one
extraction and whose score came from another would look perfectly healthy and be wrong.

A stage can also finish **partial**, which means it did its job and something inside it could
not be done. The commonest case is analyse: some rules could not run. That is not a failure
and the run continues.

## While it runs

You can leave. The run is executed by a background worker, not by your browser, and it keeps
going if you close the tab. Come back to the Runs screen and open the run to pick the
timeline up where it got to.

## Removing a run

An engagement accumulates a run from every attempt, and the list is what you scroll when you
are looking for the one you mean. An Admin can remove a run and everything it produced.

Two things worth knowing. A run that a later run was built from cannot be removed, because
that later run would be left describing an assessment that no longer exists. And work items
already published to Azure DevOps or Jira stay exactly where they are: removing the run
removes this product's record of having written them, and nothing in the client's board.

The findings screens always read one run, never a pile of them, so removing old runs is
housekeeping rather than a correction.

## When it finishes

Start at the **Overview**. It gives you the counts, the ratio, the severity spread and the
total estimate.

Then read the **not assessed** list before you read anything else, so you know what the
numbers below it do and do not cover.
