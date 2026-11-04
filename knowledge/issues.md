---
type: Workflow
title: Issues
description: How problems and features become GitHub issues — when to file, the filing bar, the issue body template, labels, areas and priority, readiness, planning a feature, and the issues.py commands.
resource:
  - tools/issues.py
---

# Issues

The issue list is the backlog the user schedules from; `/orchestrate` turns chosen issues into briefs, and an issue's
body becomes its brief word for word (`issues.py batch`). So an issue is written for the session that builds it.

## When to file

A session that finds something broken or worth improving outside its own task files a GitHub issue for later. It
doesn't fix it in passing, and it doesn't spawn a background-task chip.

- **Search first**: `gh issue list --search "<keywords>" --state all --limit 50`. Comment on an existing issue rather
  than filing a duplicate.
- **File**: `gh issue create --title "<what's wrong, in a phrase>" --label <labels> --body-file <file>`, the body in the
  template below (the merge guard refuses `gh issue create` without `--body-file`). Run it from inside the checkout:
  `gh` finds the repo from the git remote.
- **Under `/orchestrate`**, children don't file: they list what they found under `Issues found`, and the orchestrator
  de-duplicates and files once.
- Trivial fixes inside files the task already touches are part of the task.

### The filing bar

**File an issue only when its body is enough to build it in full, or nearly.** It clears the bar when:

- **Change** (or **Seen**) names the concrete behaviour or look wanted, not a direction. "The chart's range selector
  offers 1, 5 and 10 years and remembers the last pick" clears it; "improve the chart" doesn't.
- **Test** names at least one pass case and one fail case.
- **Done when** is something you could see or check.
- What's still open is small: a first-pass number exposed as a setting, or one question with options (then
  `needs-decision`). Unknown *code* is fine (`Where: unknown: investigate`, `needs-plan`); unknown *intent* isn't.

A bug clears it when **Seen** carries evidence: steps, a failing test, a log line, a screenshot described. Below the
bar, don't file: ask the user the questions that would make it buildable.

## The issue body

```markdown
**Seen:** <bug: what was seen, with the evidence (a failing command, a test name, a log line, the user's words)>
**Change:** <enhancement or tech-debt, instead of Seen: what should change, and why it matters>
**Where:** `<path>:<line>` per site, or `unknown: investigate`
**Fix:** <the change; the filer's or user's suggestion first. Options, if it's open (then `needs-decision`)>
**Test:** <per behaviour: the pass case and the fail case; "UI change: in-game screenshot (the in-game check)" when it draws>
**Done when:** <the observable end state>

Related: #<n>. Found by: <session, playtest, review>.
```

The **Test** line matters most: the testing rule makes the request the test contract.

## Labels

Exactly one `area:<name>` and exactly one type: `bug`, `enhancement` or `tech-debt`. Then any of:

- `needs-decision` when the fix changes behaviour the user hasn't decided: held back from batches until they do.
- `needs-plan` for a feature whose code isn't worked out yet: `/plan-feature` slices it first.
- `tracking` for a planned feature's parent: its slices are the work, so batches skip it.
- Optionally a priority (below).

## Areas and priority

Every open issue carries **exactly one `area:` label**, where the work lands: `chart` (the Economy panel's line chart),
`taxation` (density taxes), `ui` (other UI), `mod` (plumbing, settings, saves, compatibility), `tests`, `tooling`,
`docs`. `python tools/issues.py areas` prints them; add one to `AREAS` in `tools/issues.py` (then `labels --apply`)
only when an area outgrows a batch.

Priority is optional: `priority:high` for something failing on `autodev`, blocking other work, or that the user asked
for first; `priority:low` for a nicety; no label is normal. A session never lowers a priority the user raised.

## Readiness

`issues.py` reads each issue's labels and fields and gives it a readiness, shown on the board and in a batch's README
with the tier it suggests ([model tiers](orchestration.md#briefs-and-model-tiers)):

| Readiness | When | Suggests |
|---|---|---|
| `decision` | `needs-decision` | Held back: ask the user first |
| `tracking` | `tracking` | Held back: its slices are the work |
| `plan` | `needs-plan`, or an `enhancement` with no `path:line` in its body | `/plan-feature` first, or one Opus child for a small one |
| `spelled-out` | A `path:line`, a **Test** and a **Done when** | Haiku or Sonnet, once the spelled-out lines can be written |
| `open` | Anything else | Opus |

## Planning a feature

A feature issue says what the player should get; [`/plan-feature`](../.claude/skills/plan-feature/SKILL.md) turns it
into code once, on Opus: it posts the plan on the issue, files each slice in the template above with
`Part of #<parent>`, marks the parent `tracking`, and hands the slices to `/orchestrate`. The user approves the plan
before anything is filed.

## Commands

| Command | Does |
|---|---|
| `python tools/issues.py board [--area A]` | Open issues grouped by area, in batch order, each with its readiness |
| `python tools/issues.py triage` | Issues without exactly one area and one type (exit 1 if any) |
| `python tools/issues.py decisions` | The `needs-decision` queue, oldest first |
| `python tools/issues.py fixed` | Open issues a merged PR says it fixes: on `master` (close them; exit 1) or on `autodev` (they close at the next promotion) |
| `python tools/issues.py batch <area> [--limit N] [--include-decisions]` | Write `tasks/<date>-<area>/`: a README and one brief per issue, for `/orchestrate` |
| `python tools/issues.py labels --apply` | Create or update the area, priority and status labels (dry run without `--apply`) |
