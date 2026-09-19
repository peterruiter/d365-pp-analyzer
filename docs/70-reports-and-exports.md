# Reports and exports

Two documents come out of a run. Both are produced from the stored results of that run, so a
report downloaded a month later says what the run said, not what the rules say today.

## The findings workbook

An `.xlsx` with a sheet per view of the same run:

- **Summary** — the counts, the ratio, the totals.
- **Findings** — every finding, with its rule, severity, category, component, evidence and
  estimate.
- **Inventory** — every component, with type, solution, craft, lifecycle and domain.
- **Not assessed** — every rule that could not run, and why.
- **Backlog** — the work items, with their acceptance criteria.

This is the one a client's architect will actually work in. It is deliberately plain: no
merged cells, no images, filters on every header row, so it can be sorted and pivoted rather
than admired.

## The assessment report

A `.pdf` written to be read by somebody who will not open the workbook. It carries the
narrative: what was read, what was not read, what was found, what it would cost, and the
roadmap.

The "not assessed" section is not an appendix. It sits near the front, because a reader who
reaches the numbers without it has been misled.

## The language a report comes out in

Reports are produced in the engagement's **report language**, which is set on the engagement
and is separate from the language you are reading the product in.

A Dutch consultant can be reading a Dutch interface and producing an English report for an
offshore team. That is the normal case, not an edge case, which is why they are two settings.

The backlog has its own language again, because the people grooming a backlog are frequently
not the people reading the report.

## Downloading

Reports are listed on the Reports screen for each run. They are generated when you ask for
them rather than kept, so a report always matches the run it names.

The file name carries the client name and the run date, because a folder of files called
`report.pdf` is a folder nobody can use.
