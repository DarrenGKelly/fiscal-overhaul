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
- A UI-only change with the game already open (yours): `./tools/build.ps1 -UIOnly`, then reload the UI in game rather
  than restarting.

## 2. Launch and load

Load the computer-use tools in one `ToolSearch` (`computer-use`, max 30) and `request_access` for Steam and
Cities: Skylines II.

1. Start the game through Steam: `Start-Process steam://rungameid/949230` (PowerShell). The launcher (Paradox) may
   show first: press its Resume or Play button. Startup takes a minute or two: wait with `wait`, screenshot sparingly.
2. Main menu: Load Game, pick the save the plan names (else the most recent), Load. Wait for the city to render.
3. Pause the simulation (Space) unless the check needs it running, so screenshots are comparable.

A crash, an error dialog or a mod-load warning at any point is a finding: screenshot it, and read the newest log in
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs` (grep for `FiscalOverhaul` and `Exception`).

## 3. Open the panel and capture

1. Open the Economy panel (the toolbar's money icon, or its hotkey) and the tab the change touches: **Budget** for the
   line chart, **Taxation** for the density rows.
2. One screenshot per thing the change should show: the default view, then each filter, range or hover state it adds.
   Use `zoom` for small text (axis labels, tooltips).
3. Judge each against the brief or the hand-back's `Expect`, one verdict line each: layout and overlaps, the game's look
   (fonts, colours, spacing like the vanilla tabs beside it), the data plausible for this city, nothing clipped or
   unstyled. Judge; don't pixel-diff. Read each image once.

## 4. Close and report

Quit from the game's menu (Main Menu, then Quit), then confirm `./tools/check-batch-status.ps1` says `Game: not
running`. Close only an instance you launched.

Report: the build tested (branch and commit), the save, one verdict line per screenshot, and any crash or log
exception with its first line. A problem outside the change goes to the user as a suggested issue
([issues.md](../../../knowledge/issues.md#the-issue-body)), not a fix.
