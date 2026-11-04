---
type: Workflow
title: Testing
description: How Fiscal Overhaul is tested — the testing rule, where pure logic lives, running the xunit and vitest suites and the compile check, the in-game screenshot check, and the tests of the tools themselves.
resource:
  - tools/test.ps1
  - Tests
---

# Testing

## The rule

- **Every feature ships with tests**, and every bug fix with a regression test that fails without the fix. Each test
  has a pass case and a fail case. A brief or issue is the test contract: each behaviour it asks for gets a test that
  checks exactly that.
- **Pure logic lives in testable classes**, so tests need neither the game nor the UI host: C# in `Mod/Logic/` (no
  game or Unity types; `Tests/` compiles it in directly), TypeScript in plain modules beside their components
  (`UI/src/chart/chart-math.ts`, `UI/src/taxation/density.ts`) with a `*.test.ts` next to them. A system or component
  then only wires game data to that logic.
- **UI changes need an in-game screenshot**, looked at: `/playtest` deploys, opens the panel in Cities: Skylines II and
  judges what it shows. The game is one shared instance
  ([orchestration.md](orchestration.md#the-shared-game-instance)).

## Running tests

| What | Command | Needs the game? |
|---|---|---|
| Compile check (C# mod builds, UI type-checks) | `./tools/build.ps1 -NoDeploy` | no: deploys to a temporary folder |
| All unit tests | `./tools/test.ps1` | no |
| C# only / UI only | `./tools/test.ps1 -CSharpOnly` / `-UIOnly` | no |
| A named C# test | `./tools/test.ps1 -CSharpOnly -Filter "FullyQualifiedName~DensityTaxTable"` | no |
| In game | `./tools/build.ps1`, then `/playtest` | yes, and the Mods folder |

- `tools/test.ps1` runs `dotnet test Tests` (net10.0) and `npm test` in `UI/` (vitest, with `CI=true` so it runs once).
  A part that doesn't exist yet is skipped with a note (exit 0) unless asked for by flag (exit 1). Exit 1 on any
  failure. By hand: `dotnet test Tests`, `npm test --prefix UI`, `npx tsc --noEmit` in `UI/`.
- **The toolchain.** Node and the .NET 6 runtime are per-user installs, not on PATH:
  `%LOCALAPPDATA%\fiscal-overhaul-tools\node-v22.*-win-x64` and `...\dotnet6`. `tools/build.ps1` and `tools/test.ps1`
  put the newest `node-v*` first on PATH; `build.ps1` also sets `DOTNET_ROOT` to `dotnet6`, which the game's
  `ModPostProcessor.exe` needs (without it the build fails with "You must install or update .NET"). By hand, do the
  same first.
- A fresh worktree has no `UI/node_modules`; both scripts run `npm ci` first.
- Unit tests and the compile check are safe in any session at any time, in parallel. Deploying and the in-game check
  are not ([the shared game instance](orchestration.md#the-shared-game-instance)).

## Tool tests

Every script under `tools/` has a test twin (`tools/test-<name>.py`, `.ps1` or `.sh`), and the hooks have
`tools/test-git-hooks.sh`. They run against throwaway repos and stubs, never the network or the game. Run the twin
after changing a script:

```
python tools/test-okf.py; python tools/test-check-doc-links.py; python tools/test-check-skills.py
python tools/test-issues.py; python tools/test-promote.py
sh tools/test-git-hooks.sh
./tools/test-land-pr.ps1; ./tools/test-claude-merge-guard.ps1; ./tools/test-test.ps1
./tools/test-new-test-batch.ps1; ./tools/test-check-batch-status.ps1
./tools/test-prune-worktrees.ps1; ./tools/test-remove-worktree.ps1
```

`tools/land-pr.ps1` runs them for you before landing: for each `tools/` or `.githooks/` file a PR changes, its
`test-<stem>.py` and `.sh` (and `test-git-hooks.sh` for `.githooks/`), plus the PowerShell suites listed in its
`$LandingPowerShellSuites`. `tools/check-doc-links.py` fails a script with no twin unless `NO_TEST_TWIN` gives a reason.
