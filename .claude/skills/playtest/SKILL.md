---
name: playtest
description: Turn the user's playtest notes on a Fiscal Overhaul build (often voice-to-text) into grouped, de-duplicated GitHub issues, and comments on issues that already cover a note. Use when the user says /playtest, "file these from my playtest", "make issues from my testing", "playtest notes", or pastes bugs and tweaks from playing the mod. Files issues only; never launches the game.
---

# Playtest → issues

The user plays a build, dictates a list, and wants it in the backlog. The list is rough: one note can hold two
problems, three notes can be one fix, and some are already filed. The job is to turn it into the fewest issues a fix
session can pick up cold, each with what was seen, what's wanted and how to test it.

The mistakes that cost the most: treating an open issue as unbuilt (issues merged into `autodev` stay open until
promotion), and **launching the game to look for yourself**. The user already played it; this skill doesn't touch the
game or the code. Checking a change in game is a different job:
[testing.md § The in-game check](../../../knowledge/testing.md#the-in-game-check).

No back-and-forth: ask only when a note has two readings that lead to different issues.

## 1. Pin the build and the backlog

```bash
git fetch -q origin
git log origin/autodev --oneline -20
powershell -NoProfile -File tools/check-batch-status.ps1 | grep "Deployed mod"
python tools/issues.py board
gh pr list --base autodev --state merged --limit 60 --json number,title,body -q '.[] | "#\(.number) \(.title[0:60]) -> \([.body | scan("(?i)(?:fixes|part of) #[0-9]+")] | join(", "))"'
```

- `Deployed mod` names the branch and commit the user played (when `tools/build.ps1` deployed it); the autodev log
  and the PR list say what that build contains. PR titles carry only the PR number, so read the `Fixes #n` /
  `Part of #n` lines. An open issue fixed there is **built, not pending**: a note about it is feedback on the build
  (a comment, or a new issue that links it).
- Read the board by eye before any keyword search (it is grouped by area: read the areas your notes touch). Notes and
  issues use different words ("the graph" vs "line chart"); a scan catches overlaps that `--search` misses. Then
  sweep every note's keywords in one call and `gh issue view <n>` the candidates:
  ```bash
  for q in "tooltip" "span" "density"; do echo "== $q"; gh issue list --search "$q" --state all --limit 8 --json number,title,state -q '.[]|"#\(.number) [\(.state)] \(.title)"'; done
  ```

## 2. Ground each note

For every note, find the feature it's about and what was decided for it:

- **The feature doc.** `knowledge/impl/` has one doc per feature ([budget-chart.md](../../../knowledge/impl/budget-chart.md),
  [density-taxes.md](../../../knowledge/impl/density-taxes.md)) with the user's earlier decisions. A note that
  contradicts a decided behaviour is a **bug**; a note that changes one is a **decision** (step 3).
- **The user's vocabulary.** Words like "the side panel" or "the filters" map to a component; one
  `git grep -il <term> origin/autodev -- UI/src Mod` is enough to name the thing in the issue. Don't read deeper: the
  fix session will. Use the game's own names (Budget tab, Taxation tab, Revenues, Service Upkeeps) in titles.

## 3. Group, and record decisions

- **One issue per fix a session would do in one go:** same feature, same surface, same reviewer look. Several new
  options on one panel are one issue.
- **Split a bug out of an enhancement group** when it can ship on its own (a layout bug vs new filters on the same chart).
- **A different system is a different issue** even when the symptom is in the same place (wrong numbers from
  `BudgetHistoryUISystem` are `area:mod`; the same numbers drawn wrong are `area:chart`).
- **A note an open issue already covers gets a comment** on that issue, not a new one. Say what the playtest added.
- Expect roughly half as many issues as notes.
- **A group below the [filing bar](../../../knowledge/issues.md#the-filing-bar) isn't filed.** If the notes give no
  concrete behaviour, test or done-when ("make it better", "needs polish"), list it under Not filed in the report with
  the questions that would make it buildable.
- **Playtest notes are the user's decisions**: filing them confirms them. When a note adds or reverses a behaviour,
  write it in the issue as `**Decided** (user, playtest YYYY-MM-DD): <rule>. Replaces "<old wording>" in
  knowledge/impl/<doc>.md § <section>.` The fix session updates that doc in the same change (CLAUDE.md's rule), so
  don't edit docs here. A note that leaves the behaviour open gets `needs-decision`.

## 4. File

Write each body to a scratchpad file and file with `--body-file` ([labels and areas](../../../knowledge/issues.md#labels):
one `area:`, one type, `needs-decision` only when a note leaves the behaviour open, `needs-plan` for a new feature).
Titles say the problem or the change in a phrase.

```bash
gh issue create --title "<phrase>" --label "bug,area:chart" --body-file <scratchpad>/<name>.md
gh issue comment <n> --body-file <scratchpad>/c<n>.md
```

With more than a handful, write the bodies from one Python script made with the Write tool (a long bash heredoc of
markdown breaks on quotes), each to `issues/<key>.md` plus a `key<TAB>labels<TAB>title` line in `issues/index.tsv`,
then file them in one loop:

```bash
while IFS=$'\t' read -r key labels title; do url=$(gh issue create --title "$title" --label "$labels" --body-file "$S/issues/$key.md"); echo -e "$key\t${url##*/}\t$title"; done < "$S/issues/index.tsv"
```

The body is the [issue template](../../../knowledge/issues.md#the-issue-body), filled from the playtest:

- **Seen** is what the user saw, in their terms (an enhancement writes **Change**, or a list of options). `gh` can't
  attach images and agents read issues as text, so describe any screenshot the user attached in Seen, and tell the
  user which issues they can drag the image into on GitHub.
- **Where** only if the user said or one grep showed it; otherwise `unknown: investigate`. Don't hunt causes.
- **Fix** is what should happen, the user's own suggestion first.
- **Test** and **Done when** are what the user expects to see, as a pass case and a fail case ("UI change: in-game
  screenshot (the in-game check)" when it draws). The testing rule makes the user's request the test contract, so name
  the behaviour each test pins.
- The footer:

  ```markdown
  Related: #<built-by>, #<neighbour>.
  Found by: the user's playtest of `<branch>` at <sha> (YYYY-MM-DD).
  ```

## 5. Report

One list: new issues (number, type, one line), comments on existing issues and why, decisions recorded (which issue,
which doc they replace), and notes not filed with the questions that would make each buildable. Then stop; the user
schedules the fixes (`python tools/issues.py board`, `/orchestrate`).
