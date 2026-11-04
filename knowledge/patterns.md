---
type: Convention
title: Patterns
description: Where to start when adding something to the mod — the how-to-extend table (UI panels, C# systems, pure logic, settings, tools, docs) and the code conventions every change follows.
---

# Patterns

## Start here: how to extend

`tools/okf.py index` copies this table into `CLAUDE.md`; edit it here.

<!-- how-to-extend:start -->
| To add | Start from |
|---|---|
| A UI panel, tab or vanilla UI change | [cs2-ui-modding.md § Reusing and replacing vanilla UI](cs2-ui-modding.md#reusing-and-replacing-vanilla-ui) (TODO main session: a step-by-step recipe); the Budget tab as the example: [impl/budget-chart.md](impl/budget-chart.md) |
| A C# simulation or UI system | [cs2-ui-modding.md § Scheduling simulation systems](cs2-ui-modding.md#scheduling-simulation-systems) (TODO main session: a step-by-step recipe); the example: [impl/density-taxes.md](impl/density-taxes.md) |
| Logic that needs tests | [Pure logic](#pure-logic), and [testing.md](testing.md#the-rule) |
| A setting | `Mod/Setting.cs` (TODO main session: a recipe) |
| A tool under `tools/` | [repo-structure.md § tools/](repo-structure.md#tools), with its `test-<name>` twin ([testing.md § Tool tests](testing.md#tool-tests)) |
| A knowledge doc | [knowledge-format.md](knowledge-format.md), then `python tools/okf.py index` |
<!-- how-to-extend:end -->

## Pure logic

Anything that can be computed without the game (sums, series, classifications, rate tables, formatting) lives in a
class or module with no game types:

- C#: `Mod/Logic/`, plain classes over plain inputs (arrays, numbers, small structs). No `Unity.*`, `Game.*` or
  `Colossal.*` usings: `Tests/` compiles these files into a net10.0 project that has no game DLLs, so a game type there
  breaks the test build. A system reads the game's data, hands it to the logic, and writes the result.
- TypeScript: a plain module beside its component (`chart-math.ts` beside `line-chart.tsx`), no React or `cs2/*`
  imports, with a `*.test.ts` beside it for vitest.

## Conventions

- Bindings and triggers are in group `fiscalOverhaul` ([cs2-ui-modding.md](cs2-ui-modding.md#reusing-and-replacing-vanilla-ui)).
- A value the user may want to tune is one constant or one setting, named, not a literal repeated across files.
- Vanilla modules the UI borrows are listed in one place (`UI/src/vanilla.ts`), so a game patch is checked there.
