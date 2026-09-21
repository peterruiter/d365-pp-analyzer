# Estimates and the backlog

## Where a number comes from

Every estimate is a range in hours, and every estimate names which of three layers produced
it:

- **Band default** — the estimate band the rule declares in the catalogue: trivial, small,
  medium, large. A quick scan uses these throughout, which is what makes it quick.
- **Model** — an estimate produced per finding by a language model, from the finding's
  evidence and the component's complexity. This is what an assessment run adds.
- **Engagement override** — a number a human set, which beats both.

The layer is shown next to every estimate. A client asking "where did 40 hours come from" is
asking a reasonable question and should get a specific answer.

## Fixed costs

Some costs are per engagement rather than per finding: environment setup, a regression pass,
a handover. Those come from the contract and are shown separately from the sum of the
findings, because adding them into a per-finding total makes the per-finding numbers wrong.

## The complexity rating

Components are rated simple, medium or complex from measures declared in the contract — a
flow's action count, an app's control count, an assembly's size. The rating feeds the model
estimate and the customisation chart.

A component whose measure was not reachable is rated **unrated** rather than simple.

## The backlog

One work item per finding would produce four hundred tasks nobody grooms. One per rule would
lose the evidence, which is the part a client is buying.

So the backlog is grouped the way a consultant would have grouped it by hand:

- an **epic** per category,
- a **feature** per rule that has enough findings to need one,
- a **story** per finding that deserves naming,
- and trivial findings **batched** into a single task per rule.

Each item carries acceptance criteria in given/when/then form and a test requirement, both
from the contract rather than written per item. They are produced in the engagement's backlog
language, which is set per engagement and is separate from the language you read the product
in.

## Publishing

A publish writes the chosen items into an Azure DevOps project, a Jira project or a GitHub
repository, picked at the time of publishing rather than stored on the connection. It
re-analyses nothing: it publishes the items that were chosen and nothing else.

Each item carries a deterministic key derived from the engagement and the item's own key, so
publishing twice updates what is there rather than creating a second copy of everything. It is
a tag in Azure DevOps and a label in Jira and GitHub, which is the one field all three have
without anybody configuring it first.

The three targets do not offer the same shapes and the product does not pretend they do. Azure
DevOps is asked which work item types the project has; Jira is asked for its issue types, and a
level it does not have lands flat rather than failing; GitHub has no types at all, so ours
become labels and the parent link is a sub-issue where the repository supports one. Nothing is
ever closed or reopened on any of them.

You can run a publish as a dry run first, which reports what it would create and update
without writing anything.
