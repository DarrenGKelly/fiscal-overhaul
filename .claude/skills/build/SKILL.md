---
name: build
description: Give the user a build of Fiscal Overhaul to play or send — by default the latest origin/autodev, built in the dedicated build worktree (never their checkout) and deployed into the game's Mods folder, or zipped into Builds/. Use when the user says /build, "make me a build", "give me the latest autodev", "build master", "a zip to send", or wants to play what landed.
---

# Build

The user wants something they can play or send: no tests, no branch, no PR. One script does the work; this skill is
the defaults and the reading of the request. The mistakes that cost the most: **building the user's checkout** (it
holds their work in progress, not what landed; `-Ref` builds in `.claude/worktrees/build` instead), and **deploying
into a running game** (it locks the mod's DLL, and the game is one shared instance:
[the shared game instance](../../../knowledge/orchestration.md#the-shared-game-instance)). The script refuses that.

A session checking its own change uses `tools/build.ps1` directly, not this skill
([testing.md § Running tests](../../../knowledge/testing.md#running-tests)).

**On Linux** (the session's platform is linux, or the user says "here" on the Linux PC) `build.ps1` can't build: the
mod only builds on Windows. Use `sh tools/build-linux.sh` instead ([§ On Linux](#on-linux)).

## 1. Read the request

| The user says | Run |
|---|---|
| just `/build`, "the latest autodev", "what landed" | `./tools/build.ps1 -Ref origin/autodev` (deploys to the Mods folder) |
| "of master", "of the release", a branch or PR | `-Ref origin/master`, `-Ref origin/<branch>` (always `origin/`: a local ref goes stale across worktrees) |
| "to send", "zip it", "for someone else" | add `-Zip`: builds into a temporary folder and writes `Builds/FiscalOverhaul-<version>-<commit>.zip`; never touches the game, so it works while the game runs |
| "a debug build" | add `-Configuration Debug` |
| "and run it", "launch it" | after the deploy, start it through Steam: `Start-Process steam://rungameid/949230` (never `Cities2.exe`; [cs2-ui-modding.md § Logs](../../../knowledge/cs2-ui-modding.md#logs)) |

Ask nothing else; the defaults are the user's.

## 2. Build

```
./tools/build.ps1 -Ref origin/autodev [-Zip] [-Configuration Debug]
```

It fetches, checks the ref out detached in `<main checkout>/.claude/worktrees/build` (installing `UI/node_modules`
there the first time), builds with the per-user Node and .NET 6 (`%LOCALAPPDATA%\fiscal-overhaul-tools`), and stamps
`build-info.txt` with the ref and commit. The post-processor prints hundreds of lines; read the `build:` lines.

- Exit 2 with `Cities: Skylines II is running`: ask the user to close the game, or offer `-Zip`. Never close it yourself.
- Exit 2 with `Unknown ref`: name it as `origin/<branch>`.
- `You must install or update .NET`: the .NET 6 runtime is missing from `%LOCALAPPDATA%\fiscal-overhaul-tools\dotnet6`;
  tell the user, don't install runtimes yourself.
- A C# error: read the first `error CS` line, not the whole log.

### On Linux

```
sh tools/build-linux.sh [--ref origin/<branch>] [--configuration Debug]
```

It bundles origin's branches here (the Windows PC can't reach GitHub over SSH: its logins live in the desktop
keyring), runs `build.ps1 -Ref <ref> -Zip` on the Windows PC over SSH (`fc-windows` in `~/.ssh/config`), copies the
zip back and replaces the Proton prefix's `Mods/FiscalOverhaul` with it. The defaults table above maps to `--ref` and
`--configuration`; there is no `-Zip` (the zip stays in the Windows checkout's `Builds/`).

- Exit 2 with `is running`: the game here holds the mod; ask the user to close it. Never close it yourself.
- `Couldn't copy the bundle to fc-windows`: the Windows PC is off or unreachable; tell the user.
- A Windows build failure prints its `build:` and `error CS` lines; read them as above. The installed mod is untouched.
- "and run it": `xdg-open steam://rungameid/949230`.

## 3. Report

One line: the ref and commit built, and where it went: the script's last line (`Deployed to ... (<ref> at <commit>)`,
`Packaged <zip>` or, on Linux, `Installed to ...`). A deployed build loads at the game's next start (no hot reload). `./tools/check-batch-status.ps1`
(on Linux, the Mods folder's `build-info.txt`) shows what the game will load at any time. Nothing to commit.
