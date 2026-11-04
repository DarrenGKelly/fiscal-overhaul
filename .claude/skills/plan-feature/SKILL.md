---
name: plan-feature
description: Turn a Fiscal Overhaul feature issue into a reviewed implementation plan and spelled-out slice issues ready for /orchestrate — open questions asked, the mod and UI code mapped, the user's approval, then one issue per slice. Use when the user says /plan-feature, "plan this feature", "break this feature into slices", "spell out this issue", or a brief reads `plan`. Also called from /orchestrate.
---

# Plan a feature

A feature issue says what the player should get. Turning that into code (which game systems and UI bindings it hooks,
what is pure logic and what touches the game, what the issue leaves open, how each behaviour is tested) is the
reasoning a cheap child can't do. Do it once, here; the user reviews the plan before any code exists, and each slice
then goes to the cheapest child that can build it.

The two mistakes that cost the most: **guessing an open question** (the plan looks finished and builds the wrong
thing), and **slices that can't land alone** (one that only works with the next blocks a whole batch).

You plan; you don't write code or open a branch. The plan lives on the GitHub issue.

## 1. Read the feature

- `gh issue view <n> --json title,body,labels,comments` (comments carry later decisions).
- **Collect every open question that changes the code** and ask the user together, each with the trade-offs and a
  recommendation. Hold the slices it touches until it's answered.

## 2. Map the code

Spawn one `researcher` (background) with this prompt, filled in:

```
Read-only, in <absolute worktree path>. For the feature below, find:
1. Where each behaviour plugs in: the C# systems, UI components and bindings a change would touch, with file:line.
2. The game types, systems and vanilla UI modules it reads or replaces (knowledge/cs2-ui-modding.md).
3. What can be pure logic (Mod/Logic/, plain TS modules) and so unit-tested, and what only the game shows.
4. The knowledge docs that describe those parts (knowledge/index.md, knowledge/impl/), and the tests that cover them.
Conclusions with evidence, about 60 lines. Feature: <issue text, plus the user's answers>
```

While it runs, read the recipe for each new kind of thing the feature adds (CLAUDE.md's "how to extend" table).
Before building on a claim the researcher makes, grep it.

## 3. Design

Write `<scratchpad>/plan.md`:

- **Approach**, one paragraph: the pieces and how they connect, what runs in the simulation, what in the UI, and the
  binding between them.
- **Decisions you made**, each with the alternative and why. These are what the user reviews.
- **Provisional values**, each a single setting or constant.
- **Risks**: game-update fragility (reflection, vanilla module paths), save compatibility, frame cost.
- **Out of scope**, so no slice grows into it.

## 4. Slice

Each slice is one branch and one PR, and **lands alone**: it builds, its tests pass, the game still loads (a feature
can stay hidden until its last slice shows it). Two to six slices; more means it's two features. Order them by
dependency, and name shared files so `/orchestrate` sequences them.

Write each slice as an issue in the [issue template](../../../knowledge/issues.md#the-issue-body):

```markdown
**Change:** <what this slice adds>
**Where:** `<path>:<line>` per site; a new file: its path, and the line it's wired in at
**Fix:** <the change, concretely: classes, bindings, the recipe it follows>
**Test:** <test file — name — pass case / fail case, per behaviour>; UI: the in-game screenshot it needs
**Done when:** <observable end state>

Part of #<parent>. Depends on: #<slice> | none. Contends: <shared file> | none.
```

When you could write every line without guessing, append the spelled-out brief, so the orchestrator copies it into a
Haiku or Sonnet child's prompt ([orchestration.md](../../../knowledge/orchestration.md#briefs-and-model-tiers)):

```
Tier:       spelled-out (haiku | sonnet)
Change:     <file:line — what changes there>
Tests:      <test file — name — pass case / fail case>
In-game:    <what the test agent screenshots, or none>
Docs:       <knowledge/ section that goes stale, or none>
Off-limits: <files it must not touch>
```

A slice that needs judgement in game (visual tuning against the vanilla tabs) stays on Opus: say so. The **last**
slice's **Done when** includes the parent's, and its body says `Its PR also says Fixes #<parent>`.

## 5. Review with the user

Show the plan in chat: the approach, your decisions, then the slices in order, one line each (what it lands, its tier,
what it contends). Ask for a yes or changes. **Nothing is filed before a yes.**

## 6. File

1. Labels exist once: `python tools/issues.py labels --apply`.
2. File the slices **in order**, so each `Depends on` can name a number:
   `gh issue create --title "<parent title>: <slice>" --label enhancement,area:<area> --body-file <scratchpad>/slice-<k>.md`.
   Add `needs-decision` to one whose question is still open.
3. Post the plan on the parent, ending with a checklist of the slices (`- [ ] #<n> <slice>`):
   `gh issue comment <parent> --body-file <scratchpad>/plan.md`.
4. Mark the parent: `gh issue edit <parent> --add-label tracking --remove-label needs-plan`.
5. Check: `python tools/issues.py board --area <area>` shows each slice with its readiness and the parent as `{tracking}`.

## 7. Hand back

Reply with the parent and its slices in order (number, readiness, tier), the questions answered, and those still
open. To build them: `/orchestrate` the slices (`python tools/issues.py batch <area>` picks them up).
