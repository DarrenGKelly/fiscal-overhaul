---
type: Convention
title: Knowledge format
description: How knowledge/ is kept as an Open Knowledge Format (OKF) v0.2 bundle — frontmatter and the type vocabulary, generated indexes and the how-to-extend copy, the log, size budgets, and the checker's commands.
resource:
  - tools/okf.py
  - tools/test-okf.py
---

# Knowledge format (OKF)

`knowledge/` is a bundle in the [Open Knowledge Format](https://github.com/GoogleCloudPlatform/open-knowledge-format) v0.2: a folder of Markdown "concepts", each opening with YAML frontmatter, plus a generated `index.md` per folder and a `log.md`. The point is **progressive disclosure**: read [index.md](index.md), pick the doc you need from its one-line description, and open only that one.

## Frontmatter

Every doc except `index.md` and `log.md` opens with:

```yaml
---
type: Implementation          # required, from the vocabulary below
title: Budget tab line chart  # required by us
description: One sentence saying what the doc covers, written for someone choosing whether to open it.
resource:                     # optional: repo-relative path(s) of the code or folder the doc describes
  - Mod/Systems/BudgetHistoryUISystem.cs
---
```

- **`type`, `title`, `description`** are required here (OKF requires only `type`). The description is what the index shows, so write it for someone deciding whether to read the doc.
- **`resource`** names the code the doc describes, as **repo-relative** paths (a list and repo-relative paths are this repo's extension of OKF's single URI). The checker fails when a path no longer exists: a doc drifting from moved or deleted code. Prefer the folder or the one or two central files.
- **`verified`** marks human review: `verified: { by: human:<name>, at: <ISO instant> }`. Only the user writes a `human:` entry, or an agent at their explicit request for that doc. A checker that confirms a doc against the code may add `{ by: process:<name>, at: ... }`.
- **`status`** (`draft` | `stable` | `deprecated`, absent means stable), **`stale_after`** (an ISO instant; the checker warns once it has passed), **`tags`** and **`sources`** follow the OKF spec when used.
- **`generated` is not used.** Git records who wrote what and when; the checker warns if one appears.

The checker's YAML parser reads a subset: plain or quoted scalars, `[flow, lists]`, `{flow: maps}`, and block lists of scalars or maps. Quote a value containing `: ` or ` #`. It fails loudly on anything else.

## Types

| Type | For | Examples |
|---|---|---|
| `Reference` | How the code and the game are put together, as they are | [cs2-ui-modding.md](cs2-ui-modding.md), [repo-structure.md](repo-structure.md) |
| `Workflow` | How work is done: process, rules, commands | [git-workflow.md](git-workflow.md), [testing.md](testing.md) |
| `Convention` | Rules for a body of docs or code | this doc, [patterns.md](patterns.md) |
| `Implementation` | One feature's code and the decisions behind it | `impl/` |

Add a type only when none of these fits, in `TYPES` in `tools/okf.py` and here in the same change.

## Indexes are generated

Each folder's `index.md` is **written by `python tools/okf.py index`, never by hand**: sections by type, entries sorted by title, each `* [Title](link) - description` from the doc's frontmatter, then the subfolders (their one-liners live in `FOLDER_DESCRIPTIONS` in the tool). OKF allows no prose in an index, so anything a folder needs to say goes in a concept doc. `check` fails when an index differs from what `index` would write.

**The how-to-extend table is generated too.** Its one source is the table between the `<!-- how-to-extend:start -->` and `<!-- how-to-extend:end -->` markers in [patterns.md](patterns.md#start-here-how-to-extend). A whole-bundle `okf.py index` copies it into `CLAUDE.md` between the same markers (links rebased; `EXTEND_COPIES` in the tool), and a whole-bundle `check` fails a copy that differs, so edit the source and run `index`, never the copy.

**A copied step has one owner.** A rule stated in several files drifts one edit at a time, so a step's wording lives in one file and the rest link it. `OWNED_PHRASES` in `tools/okf.py` pins each such rule to its owner (`land-pr`'s refusals to [git-workflow.md](git-workflow.md#autonomous-sessions-autodev)), and a whole-bundle `check` errors on the phrase anywhere else in `knowledge/` (not the log), the skills, the agents or `CLAUDE.md`.

**The folder map.** A whole-bundle `check` fails when a top-level folder of `Mod/`, `UI/src/` or `Tests/` (build output skipped) isn't named in [repo-structure.md](repo-structure.md) as `` `<folder>/` ``.

## Where dated history goes

Rule docs say what is true now. A dated record of a change (an incident, a migration, why a rule was added) goes in [log.md](log.md): `## YYYY-MM-DD` headings newest first, one bullet per entry (`* **Update**: ...`, `* **Creation**: ...`, `* **Deprecation**: ...`) linking the doc it concerns.

**`check` lints for it.** A line in a rule doc (not `log.md` nor fenced code) is dated history when it has a "(user, <date>)" attribution, "decided (on) <date>", "removed/added/changed on <date>", a line that opens with "<date>:", a "Before/Until/since <date>" phrase, or an arrow with a date. That is an error. History by issue number ("until #N") only warns: say what is true now, unless the line names a real dependency ("until #N lands").

**Writing an entry**: add it to `log.md`, at the top, under today's heading. When `check` reports `log.md` over its budget, run `python tools/okf.py rotate-log` first: it moves whole days, oldest first, into a monthly archive `log/YYYY-MM.md` (never today's), and repoints links to the moved days. `check` also fails a bullet that appears twice in one file, which is what a union merge of two branches leaves behind.

## Size budgets

Every doc has a byte budget (LF endings): 40 KB (`DOC_BUDGET`), `impl/` 35 KB, skills 25 KB, and `CLAUDE.md` 18 KB (`CLAUDE_MD_BUDGET`), since every session loads it. Over budget, split the file per topic or tighten lines; raising a budget is the user's decision. A new `impl/` doc born above 70% of its budget warns ("split by topic now"). A whole-bundle `check` prints a size line (the bundle's KB per folder, and the change since `origin/master`); for a number to quote (`/promote`), `okf.py growth <base> <tip>` compares two refs.

Budgets are merge-safe, because two branches can each fit and overflow together once both land:

- **Pre-commit** runs `okf.py check --staged -` with the commit's paths: a budget error fails the commit only for a doc it stages; an unstaged one over budget prints a `[budget]` warning. A staged doc that grew and ends within the last 0.5% of its budget (`GROW_INTO_CAP`) is an error, and one within 2% (`NEAR_CAP`) warns.
- **The pre-merge-commit hook** runs `okf.py budget-tree` on a merge's tree and warns, never blocks.
- **`land-pr.ps1`** runs that tree's `okf.py budget-tree` on the PR merged with `origin/autodev` ([git-workflow.md](git-workflow.md#autonomous-sessions-autodev)).
- **A generated `index.md`** is never refused (a cap would block adding a doc); `tools/test-okf.py` fails when the real `impl/index.md` outgrows `INDEX_BUDGET` (30 KB).
- **The jam list:** every `check` ends with a line naming each doc within 1% of its cap, fullest first. Those are the files to split next.

## Implementation docs

`impl/` holds one doc per feature: what the code does, how it's structured, and the decisions behind it, with `resource:` pointing at its central files. Game facts several features share (how the game's UI modules are found and replaced, how systems are scheduled) belong in [cs2-ui-modding.md](cs2-ui-modding.md), not in each impl doc. `impl/` stays flat: no topic subfolders.

## Commands

```
python tools/okf.py check            # whole bundle; exit 1 on errors (warnings don't fail)
python tools/okf.py check knowledge/impl/budget-chart.md
python tools/okf.py index            # regenerate every index.md (and CLAUDE.md's how-to-extend table)
python tools/okf.py rotate-log       # log.md full: move its oldest days to log/YYYY-MM.md
python tools/okf.py growth origin/master origin/autodev   # knowledge/ bytes added between two refs
python tools/okf.py section knowledge/cs2-ui-modding.md#building   # print one heading's section; exit 1 on a missing anchor
python tools/test-okf.py             # the tool's tests, including a check of the real bundle
```

Run `check` after editing any doc here, and `index` whenever you add, rename, retitle or re-describe one.
