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
| Work items | Azure DevOps | The team who will do the work |

## The rule that shapes everything else

An evidence source the connection cannot reach produces a *not assessed* record, by rule, by
name, in the run header and on every export.

Never a pass. Never zero findings. Never silence.

A report saying a client has no technical debt, when the truth is that nobody could read
their environment, is the single most damaging thing this product could produce. It would be
believed, it would be quoted, and it would be wrong.

## Where to go next

| Question | Document |
|---|---|
| What do I need before I start | `02-prerequisites.md` |
| How do I connect to a client environment | `05-connecting-an-environment.md` |
| What does each rule mean | `06-the-rule-catalogue.md` |
| How are the estimates produced | `09-estimating.md` |
| How do I publish to Azure DevOps | `12-publishing-to-devops.md` |
| Why is it built this way | `19-architecture-decisions.md` |
