# Reading the findings

## Categories

Every rule sits in one of ten categories. The findings screen groups by them.

| Category | Is about |
|---|---|
| **Lifecycle and deprecation** | Components Microsoft has removed, deprecated or stopped investing in. |
| **Modernisation** | Something that works and has a better answer now. |
| **Build quality** | How well the thing that exists was built: error handling, naming, structure. |
| **Architecture** | Whether logic sits where logic should sit. The low-code ratio lives here. |
| **ALM and solution hygiene** | Whether this can move between environments without somebody remembering something. |
| **Governance** | Ownership, sprawl, orphaned components, licensing exposure. |
| **Performance** | Things that are slow now, or will be at volume. |
| **Security** | Privilege, secrets and exposure. |
| **Operability** | Whether anybody would find out when it breaks. |
| **AI components** | Agents, prompts and models: whether what the estate built on AI is grounded, current and owned. |

Modernisation is the category a client most wants and the one most likely to be oversold, so
every rule in it carries a reason to leave the thing alone as well.

## Severity

Critical, high, medium, low, info. Severity comes from the rule, not from the component, and
a handler may lower it for a reason it records in the evidence.

Severity is not priority. A critical finding on a component nobody uses is less urgent than a
medium one in the middle of the daily process, and the product does not pretend to know which
is which. That judgement is yours, and the backlog is where you record it.

## Evidence

Every finding carries what triggered it. This is the part a client is buying: a claim you
cannot check in front of them is a claim that loses the room.

Open a finding and you get the specific values — the action count, the isolation mode, the
URL that was found, the number of libraries on the form. Not a restatement of the rule.

## Where a finding came from

Findings are marked **catalogue**, **checker** or **model**.

A checker finding came from Microsoft's Power Apps checker and carries Microsoft's own rule
identifier, so you can look it up in their documentation. Those rules stay current because
Microsoft maintains them, not because this product does.

Exactly one rule is decided by a language model: whether a description says anything. It
reads one description at a time, it says on every finding that a model judged it, and it
carries the model's own sentence so you can disagree with it out loud. Every other rule is a
measurement. With no model configured for the run, that rule is reported as not assessed
rather than as passing.

## Managed components

A finding against a component that arrived in a managed solution is reported, and never
estimated. It is somebody else's to fix, and estimating work on a solution you do not ship is
inventing a number. Raise it with whoever ships it.

## Overriding a finding

You can override a finding's estimate on an engagement. The override attaches to the
finding's stable key — rule plus component — so it survives a re-run. Somebody renaming a
flow does not orphan the estimate a workshop spent an hour agreeing.

## The roadmap

The roadmap places each finding on a grid: a row for the kind of work and a column for
whether it is people, process or technology, in bands from "unclutter" outward. It is a way
of showing a client the shape of the work rather than a list of 300 items.

Rules with findings and no roadmap position are reported at the bottom of the score stage, so
the grid cannot silently drop a category.
