---
name: build
description: Build Fiscal Overhaul and deploy it into the game's Mods folder, or run the compile check — the C# mod (dotnet build) and the UI module (webpack), one part or both. Use when the user says /build, "build the mod", "deploy it", "rebuild the UI", "does it compile", or before an in-game test. Refuses to deploy while Cities Skylines II is running.
---

# Build

One script does the work: `tools/build.ps1`. This skill is the reading of the request and of the result. The mistake
that costs most is deploying into a game that is running (the game holds the DLL, and another session may be testing
in it): the game is one shared instance
([the shared game instance](../../../knowledge/orchestration.md#the-shared-game-instance)), and the script refuses.

## 1. Read the request

| The user says | Run |
|---|---|
| just `/build`, "deploy it", "build the mod" | `./tools/build.ps1` (C# mod, then the UI after the deploy) |
| "does it compile", "check the build", in a batch child | `./tools/build.ps1 -NoDeploy` (temporary Mods folder; the UI is type-checked) |
| "rebuild the UI", "just the UI" | `./tools/build.ps1 -UIOnly` (copies even with the game open, but the game loads it only on restart) |
| "just the C# side" | `./tools/build.ps1 -ModOnly` (the deploy wipes the mod folder, so the deployed mod has no UI until the next full build: say so) |
| "a debug build" | add `-Configuration Debug` |

## 2. Build

Run it from the checkout the user means (a worktree deploys its own branch: the Mods folder holds whichever ran last).
The script finds the per-user Node and .NET 6 under `%LOCALAPPDATA%\fiscal-overhaul-tools` by itself and installs
`UI/node_modules` in a fresh checkout ([testing.md § Running tests](../../../knowledge/testing.md#running-tests)).

- Exit 2 with `Cities: Skylines II is running`: ask the user to close the game (or whether a `-NoDeploy` check is
  enough). Never close it yourself.
- `You must install or update .NET`: the .NET 6 runtime is missing from `%LOCALAPPDATA%\fiscal-overhaul-tools\dotnet6`;
  tell the user, don't install runtimes yourself.
- A C# error: read the first `error CS` line, not the whole log.

## 3. Report

One line: what was built, and the script's last line (`Deployed to ... (<branch> at <commit>)` or `Compile check
passed`). Anyone can see what the game will load with `./tools/check-batch-status.ps1` (it reads the deployed
`build-info.txt`). Nothing to commit.
