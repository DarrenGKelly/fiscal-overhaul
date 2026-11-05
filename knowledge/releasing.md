---
type: Workflow
title: Releasing
description: How a release reaches Paradox Mods — the version and ChangeLog stamped into the promotion, tools/publish.ps1's modes, refusals, dry run and private test uploads, the v<version> tags, the new mod's ModId, and what ModPublisher does.
resource:
  - tools/publish.ps1
  - .claude/skills/publish
---

# Releasing

A release is a promotion of `autodev` to `master` ([git-workflow.md](git-workflow.md#promoting-autodev-to-master-the-user))
that the user then uploads to Paradox Mods with [`/publish`](../.claude/skills/publish/SKILL.md). Uploading is never
automatic: the user runs it, after the promotion PR has merged.

```
/promote   -> promotion PR (stamped vX.Y.Z) -> the user merges it into master
/publish   -> ./tools/publish.ps1 -DryRun -> the user says yes -> ./tools/publish.ps1 -> tag vX.Y.Z
```

## The version and ChangeLog

`Mod/Properties/PublishConfiguration.xml` is what ModPublisher uploads: the page text, the images, `ModVersion` and
the `ChangeLog`. The promotion stamps it, so the version on `master` is always the one its promotion PR is titled:

- `python tools/promote.py plan` picks the version: the next patch after `master`'s last `vX.Y.Z - ` commit, or the
  `ModVersion` autodev already declares when that is higher (a hand-made minor or major bump, or the first release),
  and never one already published: with a `v<version>` tag at or above it, the next patch after the highest tag.
- On the promotion branch, `python tools/promote.py stamp vX.Y.Z --body <body>` writes `X.Y.Z` into `ModVersion` and
  `UI/mod.json`'s `version`, and drafts the `ChangeLog` (one line per Included PR); the `/promote` skill trims it to
  what players notice and commits it as the branch's one extra commit. It refuses a version below the declared one.
- That commit is on the promotion branch only, so the PR's head isn't in `autodev`'s history. The PR body records
  the `autodev` commit it was cut from (`Round head: <sha>`), and the next round starts there. The sync back after the
  merge brings the stamp into `autodev`.
- Nothing stamps an assembly version: the mod's csproj sets none. If one is added, stamp it too: `publish.ps1`
  refuses a csproj `Version` or `AssemblyVersion` other than `ModVersion`.

## tools/publish.ps1

`./tools/publish.ps1 [-Mode Publish|NewVersion|Update] [-Ref origin/master] [-DryRun] [-AccessLevel Private|Unlisted]`
(its help has every detail).

- **Where it builds.** It fetches, checks the ref out detached in a temporary worktree under TEMP (never the user's
  checkout), and runs `dotnet build -c Release` into a temporary Mods folder, as `build.ps1 -Zip` does. So the upload
  holds exactly the build: not what the game's Mods folder holds (`build-info.txt`, a branch someone deployed). The
  worktree and the folder are removed afterwards. It refuses while the game runs.
- **The mode follows `ModId`.** Empty: `Publish` (a new mod). Set: `NewVersion`. `-Mode Update` changes the page only
  (descriptions, images, tags, access level): nothing is built or tagged.
- **It refuses before uploading**, each refusal naming its fix: `ModVersion` other than `UI/mod.json`'s version; an
  empty `ChangeLog` for `NewVersion` or `Update`; a missing `Thumbnail` or `Screenshot` file; a template leftover
  ("This is a long description", an empty `Tag`, `ExternalLink`, `Screenshot`, `Dependency` or `RequiredDLC`); an
  `AccessLevel` other than `Public`, `Private` or `Unlisted`; and, for an upload of a version, a `v<version>` tag that
  already exists. A refusal on `master` is fixed on `autodev` and promoted again.
- **`-DryRun`** does all of that and the Release build, then prints the exact command and stops.
- **`-AccessLevel Private`** uploads with that access level for this run only, from a temporary copy of the config:
  a test upload only the account can see. Making it public later is `-Mode Update` with the config's `Public`; the
  version is already uploaded, so its tag stays.
- **After a successful upload** of a version it tags the built commit `v<version>` (an annotated tag naming the mode
  and access level) and pushes the tag. A failed build or upload tags nothing.

## The new mod's ModId

ModPublisher never writes the id back into the config; `Publish` prints `Mod published with Id=<n>`. `publish.ps1`
reports it (or a `ModId` it finds written back, should a later ModPublisher do that) and the follow-up: commit
`<ModId Value="<n>" />` to the config through a PR into `autodev`, so the next release is a `NewVersion`. Until that
lands, a second `Publish` would create a second mod; with the id committed, `Publish` is refused.

## ModPublisher

The game's toolchain publishes with `dotnet publish` and the profiles in `Mod/Properties/PublishProfiles/`
(`PublishNewMod` = `Publish`, `PublishNewVersion` = `NewVersion`, `UpdatePublishedConfiguration` = `Update`). Its
`Mod.targets` (under `%CSII_TOOLPATH%`) then runs `ModPublisher.exe <command> <config> -c <content> -v`, the content
being the deployed mod folder. `publish.ps1` runs that same command itself, so it can build into a clean folder, check
first and dry-run. What ModPublisher (`%CSII_MODPUBLISHERPATH%`, a .NET 6 program) does, read from its code:

- It **logs in with the session the game stored** (`.pdxsdk` under the game's LocalLow folder). Not logged in: launch
  the game, log in to the Paradox account, close it, run again. Never pass `-e`/`-p`/`--loginData`/`--noAutoLogin`:
  this repo never stores or asks for credentials.
- It needs `DOTNET_ROOT` on a .NET 6 runtime: `publish.ps1` sets the per-user one
  (`%LOCALAPPDATA%\fiscal-overhaul-tools\dotnet6`, `Use-LocalToolchain` in `tools/git-common.ps1`).
- It resolves `Thumbnail` and `Screenshot` paths **against its working folder**, not the config's: `publish.ps1` runs
  it from `Mod/`, as `dotnet publish` does, so paths are written `Properties/...`.
- Its quiet fallbacks are why `publish.ps1` checks first: a missing thumbnail becomes its default picture, an unknown
  `AccessLevel` becomes `Public`, an unknown `ExternalLink` type is dropped. `NewVersion` and `Update` both require
  `ModId` and a `ChangeLog`; `Publish` requires `ModId` empty or 0.
- It uploads every file in the content folder (except `.nupkg`).
