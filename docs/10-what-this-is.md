# What this is

A read-only assessment of a Power Platform estate. You point it at an environment or hand it
an exported solution file, it reads what is there, and it produces four things:

1. **An inventory.** Every component, by type, by solution, by domain.
2. **A low-code ratio.** One number, with its definition attached, because the number gets
   quoted without it.
3. **Findings.** What is deprecated, badly built, ungoverned, slow or exposed, each one with
   the evidence that triggered it.
4. **An estimate.** A range of hours per finding, with a rationale, adding to a total whose
   arithmetic you can check in front of a client.

## What it will not do

**It never writes to a Power Platform environment.** Not in any mode, and there is no setting
that changes that. You can run a discovery in a first conversation without a change advisory
board, which is the point.

The only thing it writes anywhere is work items into Azure DevOps, and only the ones
somebody picked on the backlog screen. There is a dry run that shows exactly what would
land and writes nothing.

**It is not a replacement for the Power Apps checker.** It calls the checker and folds the
results in under Microsoft's own rule identifiers. The rules it declares itself are the ones
the checker does not have: lifecycle position, debt spread across components, sprawl, and
solution hygiene.

**It is not a licensing assessment.** It reports where a premium connector is used and stops
there. It cannot see what the tenant holds, and guessing would be worse than silence.

**It is not a penetration test.** The security rules are about privilege, secrets in
definitions and organisation-level write. They are not an assessment of whether the estate
can be broken into.

## What "not assessed" means

This is the most important idea in the product, so it has its own section.

Every rule declares the evidence it needs. If the way you connected cannot reach that
evidence, the rule is reported as **not assessed**, by name, with the reason. It is never
reported as passing and never counted as zero findings.

A report that says a client has no technical debt when the truth is that nobody could read
their environment is the most damaging thing this product could produce. So the report always
carries a list of what it could not check, and you should read that list out loud in the
room.

## The demonstration estate

Everybody admitted to the product can open an engagement called **Demonstration estate**. It
is a synthetic Power Platform estate shaped like a mid-sized utility, and nothing in it comes
from a client.

The findings in it are not invented. They are produced by running the same rule engine,
scorer and backlog builder over the synthetic estate that runs over a real one. It is
read-only for everybody, so you can explore every screen without being able to break it.

Use it to learn the product, and use it to demonstrate the product before a client has given
you anything.
