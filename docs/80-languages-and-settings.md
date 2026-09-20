# Languages and settings

## Three language settings, on purpose

| Setting | Controls | Set on |
|---|---|---|
| **Interface language** | The screens you are reading now. | You. |
| **Report language** | The workbook and the PDF. | The engagement. |
| **Backlog language** | Work item titles, descriptions and acceptance criteria. | The engagement. |

They are separate because they genuinely differ in practice. A Dutch consultant reads a Dutch
interface, writes an English report for a client's group IT, and produces a Spanish backlog
for the team who will do the work.

Six languages are available throughout: English, Dutch, German, French, Spanish and Italian.

## Where your preference is kept

Against your user record in the product database, not in your browser. Your language and
your light or dark theme follow you to a second machine, and nothing is kept in local
storage.

## What is translated

The interface, the rule names and explanations, the finding text, the inventory labels and
the backlog text are all translated into all six languages.

The written guides you are reading are translated too, into the same six languages.

A guide falls back to English per guide rather than per product, so if a page is ever added
faster than it is translated you see that page in English and everything else in your own
language, rather than the whole set reverting.

## Theme

Light and dark, following your machine unless you overrule it. The choice is stored against
your user, like the language.

## Administration

Global administrators get an Administration screen with:

- **Users** — who is admitted, who is a global administrator, and who admitted them.
- **Engagements** — every engagement in the product, and who has what role on each.
- **System health** — whether the API, the database, the worker and Key Vault are reachable,
  and the version each is running.

The system health screen is the first place to look when something is not working. It
distinguishes "the worker is not running" from "the worker is running an older image", which
are two very different afternoons.
