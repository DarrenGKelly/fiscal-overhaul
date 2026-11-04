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
- **UI changes need an in-game screenshot**, looked at: [the in-game check](#the-in-game-check) deploys, opens the panel in
  Cities: Skylines II and judges what it shows. The game is one shared instance
  ([orchestration.md](orchestration.md#the-shared-game-instance)).

## Running tests

| What | Command | Needs the game? |
|---|---|---|
| Compile check (C# mod builds, UI type-checks) | `./tools/build.ps1 -NoDeploy` | no: deploys to a temporary folder |
| All unit tests | `./tools/test.ps1` | no |
| C# only / UI only | `./tools/test.ps1 -CSharpOnly` / `-UIOnly` | no |
| A named C# test | `./tools/test.ps1 -CSharpOnly -Filter "FullyQualifiedName~DensityTaxTable"` | no |
| In game | `./tools/build.ps1`, then [the in-game check](#the-in-game-check) | yes, and the Mods folder |

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

## The in-game check

How a session or the test agent checks a change in the running game with computer use. The mistakes that cost the
most: **testing a stale build** (the Mods folder holds whichever checkout deployed last) and **taking the game from
someone else** ([the shared game instance](orchestration.md#the-shared-game-instance)).

1. **Claim and deploy.** `./tools/check-batch-status.ps1`. `Game: RUNNING` and not yours: the user or another agent has
   it; ask, never close it. `Game: not running`: `./tools/build.ps1` from the checkout under test; its last line names
   the branch and commit the game will load. The game doesn't hot-reload mod UI, and a running game locks the DLL, so
   every new build needs the game closed first.
2. **Launch.** Load the computer-use tools in one `ToolSearch` (`computer-use`, max 30); `request_access` for
   `["Cities Skylines II", "paradox launcher.exe"]`, plus `"File Explorer"` (click-only) when a click fails with "The
   desktop shell is frontmost". Start through Steam only: `Start-Process steam://rungameid/949230`. `Cities2.exe`
   started directly (`open_application "Cities2"` too) hangs at startup ([cs2-ui-modding.md § Logs](cs2-ui-modding.md#logs)).
3. **Load.** The Paradox launcher opens first, often behind other windows: `open_application "Paradox launcher"`,
   screenshot, `zoom`. **Resume** loads the most recent save; **Play** goes to the main menu (Load Game, the plan's save).
   `Get-Process Cities2` shows the game has started; startup and loading take about two minutes (one long `wait`).
   Pause (Space) unless the check needs the simulation running.
4. **Capture.** Open the Economy panel (click the money total in the bottom bar) and the tab: **Budget** for the chart,
   **Taxation** for the density rows. One screenshot per thing the change should show, each filter, span or hover state
   (`mouse_move`, not a click). On the ultrawide monitor screenshot at `scale: 0.5`, then `zoom` on the panel
   (`save_to_disk` for the ones you report); axis labels and tooltips need the zoom.
5. **Judge** each against the plan's `Expect`, one verdict line each: layout and overlaps, the game's look (fonts,
   colours, spacing like the vanilla tabs), plausible data for this city, nothing clipped. Don't pixel-diff; read each
   image once. A crash, hang or error dialog is a finding: screenshot it and read the [logs](cs2-ui-modding.md#logs).
6. **Close.** Quit from the game's menu (Main Menu, then Quit); a hung instance you launched: `Stop-Process -Name
   Cities2`. Confirm `./tools/check-batch-status.ps1` says `Game: not running`. Close only an instance you launched.

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
