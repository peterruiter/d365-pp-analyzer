# Running an analysis

## The four modes

| Mode | Does | Takes |
|---|---|---|
| **Quick scan** | Reads the estate and applies every rule, using estimate bands rather than individual estimates. | Minutes. |
| **Assessment** | A quick scan, plus an estimate per finding and a groomed backlog. | Longer, and it calls a language model. |
| **Compare** | An assessment measured against an earlier run, so you can show what changed. | As an assessment. |
| **Publish** | Writes the approved backlog into Azure DevOps. Reads nothing and re-analyses nothing. | Minutes. |

Start with a quick scan. It is the mode that is safe to run in a sales conversation, and it
answers most of the questions a first meeting has.

## The stages

A run moves through eight stages, and the run screen shows where it is:

1. **Extract** — reads the estate.
2. **Checker** — submits the solution to the Power Apps checker and waits.
3. **Resolve** — joins components to each other, so the rules can ask what points at what.
4. **Analyse** — applies every rule in the catalogue.
5. **Estimate** — puts a range of hours on each finding. Skipped by a quick scan.
6. **Score** — computes the ratio, the customisation chart and the roadmap.
7. **Backlog** — turns findings into work items somebody would actually groom.
8. **Publish** — writes to Azure DevOps. Only runs in publish mode, only after approval.

A stage that fails can be retried on its own. A run that died half way through an extraction
resumes rather than restarting, because re-extracting a large estate costs an hour and
re-running a checker job costs a place in somebody else's queue.

A stage can also finish **partial**, which means it did its job and something inside it could
not be done. The commonest case is analyse: some rules could not run. That is not a failure
and the run continues.

## While it runs

You can leave. The run is executed by a background worker, not by your browser, and it keeps
going if you close the tab.

## When it finishes

Start at the **Overview**. It gives you the counts, the ratio, the severity spread and the
total estimate.

Then read the **not assessed** list before you read anything else, so you know what the
numbers below it do and do not cover.
