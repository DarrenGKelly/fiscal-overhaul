---
type: Workflow
title: Testing
description: How Fiscal Overhaul is tested — the testing rule, where pure logic lives, running the xunit and vitest suites and the compile check, the in-game screenshot check, and the tests of the tools themselves.
resource:
  - tools/test.ps1
  - tools/in-game-check.ps1
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

How a session or the test agent checks a change in the running game. The mistakes that cost the most: **testing a
stale build** (the Mods folder holds whichever checkout deployed last) and **taking the game from someone else**
([the shared game instance](orchestration.md#the-shared-game-instance)). The game is driven from the shell with
`tools/game-control.ps1` ([below](#on-windows-without-computer-use)); computer use is never needed.

**Run it with `tools/in-game-check.ps1`** (`Get-Help ./tools/in-game-check.ps1 -Full`), which does steps 1 to 4 and 6
below the same way every time, and leaves step 5, judging, to you:

```
./tools/in-game-check.ps1 -Out <dir>                       # deploy this checkout, launch, shoot, quit
./tools/in-game-check.ps1 -Out <dir> -SkipDeploy           # test what the Mods folder holds
./tools/in-game-check.ps1 -Out <dir> -Steps <plan-steps>   # plus the plan's own clicks, hovers and shots
```

- It refuses (exit 2) unless the game is free (`Game: not running`), deploys with `build.ps1` (`-Ref` passes through),
  launches through Steam, clicks Resume (scaled to the launcher's size, and clicked once more when no game window
  comes within 60 s), waits for the load, refuses a client area other than 2560x1440, and makes
  sure the save is paused from the PAUSED banner (Space only when the banner is missing, then checked again).
- The shots in `-Out`: `loaded.png` (full scale), `budget.png` (cursor parked), `budget-hover.png` (the tooltip at
  `ChartPoint`), `taxation.png`, each `-Steps` screenshot, and `quit-menu.png`/`quit-dialog.png`; `manifest.md` names
  the deployed branch and commit (`build-info.txt`), the save, each shot and what it shows, and how the run ended.
- **`-Steps`**: one step a line, `#` comments: `click|right-click|move|hover <point>`, `key <name>`, `wait <ms>`,
  `screenshot <name> <what it shows>`. A point is `@Name` (`-ListPoints` prints them) or `x,y` in client offsets.
  The steps start on the Taxation tab with the panel open; Space is refused.
- It always quits, red or green, in a `finally`: Escape, Escape, Exit, Exit to Desktop; a failed run (unknown state)
  or a quit that doesn't end the game within 30 s stops the instance. It closes only an instance it launched (one
  started after the claim) and ends with `Game: not running` (exit 3 if not).
- Both scripts need PowerShell 7 (`pwsh`; `#Requires -Version 7.0`): Windows PowerShell 5.1 refuses them.
- **The points** are client offsets in a 2560x1440 window, in one table in `tools/game-run-common.ps1`, each marked
  `measured` (clicked in a run) or `derived` (read off a reference shot). `-Point Name=x,y` corrects one for a run;
  a corrected point goes back into the table.
  The Economy tabs sit in the order Budget, Taxation, Services, Production, Loans ([UI/src/economy/tab-order.ts](../UI/src/economy/tab-order.ts)), so `TaxationTab` is the 2nd slot (x 1008) and `ServicesTab` the 3rd (x 1280).

The manual sequence (steps 1 to 6 and [the commands below](#on-windows-without-computer-use)) is the reference for
what the script does, and the way to work when it fails.

1. **Claim and deploy.** `./tools/check-batch-status.ps1`. `Game: RUNNING` and not yours: the user or another agent has
   it; ask, never close it. `Game: not running`: `./tools/build.ps1` from the checkout under test; its last line names
   the branch and commit the game will load. The game doesn't hot-reload mod UI, and a running game locks the DLL, so
   every new build needs the game closed first.
2. **Launch.** Through Steam only: `Start-Process steam://rungameid/949230`. `Cities2.exe` started directly hangs at
   startup ([cs2-ui-modding.md § Logs](cs2-ui-modding.md#logs)).
3. **Load.** The Paradox launcher opens first, often behind other windows. **Resume** loads the most recent save;
   **Play** goes to the main menu (Load Game, the plan's save). Loading takes about a minute; the save comes up
   **paused** (Resume keeps the pause it was saved with), so check before pressing Space, which toggles it.
4. **Capture.** Open the Economy panel (click the money total in the bottom bar) and the tab: **Budget** for the chart,
   **Taxation** for the density rows. One screenshot per thing the change should show, each filter, span or hover state
   (move the cursor onto it, no click). Screenshot the game window at half scale; crop a region at full scale where
   axis labels and tooltips need it. Keep the ones you report.
5. **Judge** each against the plan's `Expect`, one verdict line each: layout and overlaps, the game's look (fonts,
   colours, spacing like the vanilla tabs), plausible data for this city, nothing clipped. Don't pixel-diff; read each
   image once. A crash, hang or error dialog is a finding: screenshot it and read the [logs](cs2-ui-modding.md#logs).
6. **Close.** Quit from the game's menu (Escape closes the panel, Escape again opens the menu: Exit, then Exit to
   Desktop); a hung instance you launched: `Stop-Process -Name Cities2`. Confirm `./tools/check-batch-status.ps1`
   says `Game: not running`. Close only an instance you launched.

**Trap: a subscribed copy of the mod wins.** If the user is subscribed to the mod on Paradox Mods (a
`pdx_mods` cache such as `...\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\166297_*`), the game
registers two UI modules with id `FiscalOverhaul` and the subscribed one wins, so the check sees the old UI. Look in
`Modding.log` for a single registration, and have the user disable the subscription in the playset.

### On Windows without computer use

`tools/game-control.ps1` drives the game with Win32 input from the shell (`Get-Help ./tools/game-control.ps1 -Full`);
it needs no computer-use access and no installs. Each command prints what it did and exits non-zero with the reason
(no game window, a window that won't come to the front). The sequence, as run on the Windows PC (one 4K monitor at
3840x2160, the game windowed at 2560x1440):

```
./tools/check-batch-status.ps1                      # Game: not running
./tools/build.ps1                                   # last line: the branch and commit deployed
Start-Process steam://rungameid/949230
./tools/game-control.ps1 wait-process -Title '^Paradox Launcher$' -Timeout 120
./tools/game-control.ps1 focus -Title '^Paradox Launcher$'
./tools/game-control.ps1 screenshot -Out <dir>/launcher.png -Window -Title '^Paradox Launcher$' -Scale 0.5 -Delay 3000
./tools/game-control.ps1 click -X <x> -Y <y>        # Resume, read off launcher.png (below)
./tools/game-control.ps1 wait-process -Timeout 120  # the game's window
./tools/game-control.ps1 wait-loaded -Timeout 300   # "Loading completed" in this launch's SceneFlow.log
./tools/game-control.ps1 window                     # client: x,y,w,h, the origin for every click below
./tools/game-control.ps1 focus
./tools/game-control.ps1 screenshot -Out <dir>/loaded.png -Window -Scale 0.5 -Delay 2000
./tools/game-control.ps1 key Space                  # only if loaded.png shows it running (no pause icon)
./tools/game-control.ps1 click -X <x> -Y <y>        # the money total: the Economy panel, on Budget
./tools/game-control.ps1 move -X <x> -Y <y>         # onto a chart line: the tooltip
./tools/game-control.ps1 screenshot -Out <dir>/budget.png -Window -Scale 0.5 -Delay 1500
./tools/game-control.ps1 click -X <x> -Y <y>        # the Taxation tab
./tools/game-control.ps1 screenshot -Out <dir>/taxation.png -Window -Scale 0.5 -Delay 1000
./tools/game-control.ps1 key Escape                 # closes the panel
./tools/game-control.ps1 key Escape                 # opens the pause menu
./tools/game-control.ps1 click -X <x> -Y <y>        # Exit, then (after a screenshot) Exit to Desktop
./tools/game-control.ps1 wait-process -Gone -Timeout 90
```

- **Coordinates** are physical screen pixels. Every screenshot prints its origin and scale: screen = origin +
  pixel / scale, so a point at (497, 184) in a `-Scale 0.5` shot with origin 998,449 is `click -X 1992 -Y 817`.
  Read the PNG with the Read tool, find the target, convert. With the game window's client area at 2560x1440 these
  client offsets held (screen = the `window` client origin + offset): money total 1950,1406; Budget tab 736,306;
  Taxation tab 1280,306; a chart point around 1800,660; pause menu Exit 818,1140; Exit to Desktop 1278,800. The
  launcher's size varies between runs and its layout scales with it: Resume was at its client origin + 660,246 in a
  1282x677 launcher and + 994,368 in one about 1.5 times that. A different window size or position moves them all: take the
  screenshot first.
- **Timings** seen: the launcher's window 2 s after `Start-Process` (Steam already running); the game's window 2-3 s
  after Resume; the save loaded 55-80 s after Resume; a panel fades in over about a second (a screenshot straight
  after the click is translucent: `-Delay 1000` or more); Exit to Desktop ends the process in 3 s.
- **Waiting**: agent shells block a bare `Start-Sleep`, so wait on a condition: `wait-process` (a window, a title,
  or `-Gone`), `wait-loaded`, `screenshot -Delay`.
- **Hover and clicks work in Gameface**: `click` (a real SendInput press), and hover from a plain `move`, unlike on
  Linux; `hover` (approach in small steps) and `move-relative` also show the tooltip. `move-relative` goes through
  Windows pointer acceleration, so it lands near, not on, the offset asked.
- **Keys** go to the foreground window as scan codes (`key Space`, `key Escape`; the names are
  `System.Windows.Forms.Keys`): `focus` first, and check the `key:` line names the game's window.
- **The pause state** shows in the bottom bar (the pause icon) and as a PAUSED banner top centre; the save loads
  paused, so a reflex Space starts the simulation.
- The game window captures normally (no black frames). `focus` brought both windows forward first time; its Alt-tap
  fallback covers a window Windows won't raise.

### On the Linux PC

**On the Linux PC** (the game under Proton, X11) there are no computer-use tools; drive it from the shell. Install with
`sh tools/build-linux.sh`, launch with `xdg-open steam://rungameid/949230` (Steam then asks for a launch option: the
plain "Play", mods enabled), screenshot with `gnome-screenshot -f <file>` (2560x1440) and click with
`xdotool mousemove X Y click 1`. **Hover needs relative moves**: an absolute `xdotool mousemove` jump reaches clicks
but not Gameface's hover, so the chart tooltip and the row pop-out only appear after a few
`xdotool mousemove_relative -- 8 -1` steps onto the target. Escape closes a panel first, then opens the pause menu
(Exit, then Exit to desktop). Logs are under the Proton prefix ([cs2-ui-modding.md § Building](cs2-ui-modding.md#building)).
`build.ps1` and `test.ps1` don't run here (no Node or .NET; build with `build-linux.sh`, run the unit tests in a clone
on the Windows PC over SSH); `pwsh ./tools/land-pr.ps1 <n>` does.

## Tool tests

Every script under `tools/` has a test twin (`tools/test-<name>.py`, `.ps1` or `.sh`), and the hooks have
`tools/test-git-hooks.sh`. They run against throwaway repos and stubs, never the network or the game. Run the twin
after changing a script:

```
python tools/test-okf.py; python tools/test-check-doc-links.py; python tools/test-check-skills.py
python tools/test-issues.py; python tools/test-promote.py
sh tools/test-git-hooks.sh; sh tools/test-build-linux.sh
./tools/test-land-pr.ps1; ./tools/test-claude-merge-guard.ps1; ./tools/test-test.ps1
./tools/test-new-test-batch.ps1; ./tools/test-check-batch-status.ps1
./tools/test-prune-worktrees.ps1; ./tools/test-remove-worktree.ps1; ./tools/test-publish.ps1
./tools/test-in-game-check.ps1; ./tools/test-store-screenshots.ps1   # a stub game-control and a fake game process
./tools/test-game-control.ps1      # shows a small window at 200,200 and moves the cursor for ~40 s
```

`tools/land-pr.ps1` runs them for you before landing: for each `tools/` or `.githooks/` file a PR changes, its
`test-<stem>.py` and `.sh` (and `test-git-hooks.sh` for `.githooks/`), plus the PowerShell suites listed in its
`$LandingPowerShellSuites`. `tools/check-doc-links.py` fails a script with no twin unless `NO_TEST_TWIN` gives a reason.
