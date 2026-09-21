# 1. Overview

## What this product answers

Four questions, in the order clients ask them.

1. **What is actually in here?** Every component, by type, by solution, by domain.
2. **How much of it is low code?** A ratio, with its definition attached, because the number
   gets quoted without it.
3. **What is technical debt?** Components Microsoft has removed, deprecated or stopped
   investing in, plus the things that were built badly regardless of what they were built with.
4. **What would it cost to fix?** A range per finding, with a rationale, adding up to a total
   whose arithmetic is checkable.

## What it is not

It is not a remediation tool. It never writes to a Power Platform environment, in any mode,
and there is no setting that changes that. Everything it produces is a recommendation, a
report or a work item.

It is not a replacement for the Power Apps checker. It calls the checker, because Microsoft
maintains those rules and keeps them current, and folds the results into the same report
under the checker's own rule identifiers. The rules this product declares itself are the ones
the checker does not have: lifecycle position, debt spread across components, usage, sprawl
and solution hygiene.

It is not a licensing assessment. It reports where a premium connector is used and stops
there, because it cannot see what the tenant holds and guessing would be worse than silence.

## The two axes

Every component is classified twice, and the two are independent.

**Craft** is what the component is made of: configuration, low code, pro code, external or
content. It drives the low code to high code ratio.

**Lifecycle** is how much life Microsoft has left in it: current, dated, deprecated, removed
or preview. It drives the technical debt list.

Conflating them produces the two most common wrong conclusions in this space: that a plugin
is debt because it is code, and that a classic workflow is fine because it is low code.
Neither follows.

## What a run produces

| Deliverable | Format | Who reads it |
|---|---|---|
| Assessment report | PDF | The client |
| Findings | Excel | The consultant, in a workshop, with a filter on |
| Backlog | Excel | The delivery team, before anybody publishes anything |
| Component inventory | Excel | The appendix nobody reads and everybody asks for |
| Work items | Azure DevOps, Jira or GitHub | The team who will do the work |

## The rule that shapes everything else

An evidence source the connection cannot reach produces a *not assessed* record, by rule, by
name, in the run header and on every export.

Never a pass. Never zero findings. Never silence.

A report saying a client has no technical debt, when the truth is that nobody could read
their environment, is the single most damaging thing this product could produce. It would be
believed, it would be quoted, and it would be wrong.

## Where to go next

Every document below is also served inside the product, on the Documentation screen, in all
six languages.

| Question | Document |
|---|---|
| What is this, and what will it not do | `10-what-this-is.md` |
| How do I get in, and what can I then see | `20-getting-access.md` |
| How do I connect to a client environment | `30-connecting-an-environment.md` |
| How do I run one, and what are the stages | `40-running-an-analysis.md` |
| What does a finding mean | `50-reading-the-findings.md` |
| How are the estimates produced | `60-estimates-and-the-backlog.md` |
| What comes out, and in what language | `70-reports-and-exports.md` |
| Languages, themes and administration | `80-languages-and-settings.md` |
| What to do the first time you build it | `03-first-run.md` |
| Where the build actually is | `../STATE.md` |

This table pointed at six documents that have never existed, which is the kind of thing a
reader finds out one click at a time.
