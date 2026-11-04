---
name: playtest
description: Check a Fiscal Overhaul change in the running game through computer use — deploy the build, launch Cities Skylines II, load a save, open the Economy panel (Budget or Taxation tab), screenshot it and judge it against what the change should show. Use when the user says /playtest, "check it in game", "screenshot the panel", "does it look right in game", or a UI change needs its in-game screenshot.
---

# Playtest in game

Every UI change ships with an in-game screenshot (CLAUDE.md, Testing). Unit tests prove the logic; only the game shows
whether the panel renders in Gameface, sits where it should and reads the real city's data. This skill drives the game
with computer use and judges what it sees.

The mistakes that cost the most: **testing a stale build** (the Mods folder holds whichever checkout deployed last) and
**taking the game from someone else** (it is one shared instance:
[the shared game instance](../../../knowledge/orchestration.md#the-shared-game-instance)).

## 1. Claim the game and deploy

```
./tools/check-batch-status.ps1
```

- `Game: RUNNING` and you didn't launch it: the user or another agent has it. Ask; never close it.
- `Game: not running`: deploy from the checkout under test, `./tools/build.ps1` ([/build](../build/SKILL.md)). Its
  last line names the branch and commit the game will load; it must be the one you mean to test.
- A UI-only change with the game already open (yours): `./tools/build.ps1 -UIOnly` copies the files, but the game
  doesn't hot-reload mod UI: restart it (quit, then §2). A C# change can't even deploy while it runs (the DLL is locked).

## 2. Launch and load

Load the computer-use tools in one `ToolSearch` (`computer-use`, max 30) and `request_access` for
`["Cities Skylines II", "paradox launcher.exe"]`; add `"File Explorer"` (click-only) when a click fails with "The
desktop shell is frontmost", which happens whenever an Explorer window is in front.

1. Start the game through Steam: `Start-Process steam://rungameid/949230` (PowerShell). **Never start `Cities2.exe`
   directly** (`open_application "Cities2"` included): it hangs at startup
   ([cs2-ui-modding.md § Logs](../../../knowledge/cs2-ui-modding.md#logs)). `open_application "Cities Skylines II"`
   fails too (it can't open the steam:// link).
2. The Paradox launcher opens first and often stays behind other windows: `open_application "Paradox launcher"`,
   screenshot, `zoom` on its window. **Resume** loads the most recent save directly; **Play** goes to the main menu
   (Load Game, the save the plan names, Load). `Get-Process Cities2` shows when the game itself has started.
   Startup and loading take about two minutes: one long `wait`, then a screenshot.
3. Pause the simulation (Space) unless the check needs it running, so screenshots are comparable.

A crash, a hang, an error dialog or a mod-load warning at any point is a finding: screenshot it and read the logs
([cs2-ui-modding.md § Logs](../../../knowledge/cs2-ui-modding.md#logs): `Player.log` for exceptions, `Logs/Modding.log`
for the UI module registering).

## 3. Open the panel and capture

1. Open the Economy panel (click the money total in the bottom bar) and the tab the change touches: **Budget** for the
   line chart, **Taxation** for the density rows.
2. One screenshot per thing the change should show: the default view, then each filter, range or hover state it adds.
   On the ultrawide monitor take screenshots at `scale: 0.5`, then `zoom` on the panel (with `save_to_disk` for the
   ones the report sends); small text (axis labels, tooltips) needs the zoom. Hover states: `mouse_move`, not a click.
3. Judge each against the brief or the hand-back's `Expect`, one verdict line each: layout and overlaps, the game's look
   (fonts, colours, spacing like the vanilla tabs beside it), the data plausible for this city, nothing clipped or
   unstyled. Judge; don't pixel-diff. Read each image once.

## 4. Close and report

Quit from the game's menu (Main Menu, then Quit); a hung instance you launched: `Stop-Process -Name Cities2`.
Then confirm `./tools/check-batch-status.ps1` says `Game: not
running`. Close only an instance you launched.

Report: the build tested (branch and commit), the save, one verdict line per screenshot, and any crash or log
exception with its first line. A problem outside the change goes to the user as a suggested issue
([issues.md](../../../knowledge/issues.md#the-issue-body)), not a fix.
