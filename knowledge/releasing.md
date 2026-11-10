---
type: Workflow
title: Releasing
description: How a release reaches Paradox Mods — the version and ChangeLog stamped into the promotion, tools/publish.ps1's modes, refusals, dry run and private test uploads, the v<version> tags, the new mod's ModId, and what ModPublisher does.
resource:
  - tools/publish.ps1
  - tools/store-screenshots.ps1
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
  `UI/mod.json`'s `version`, and gives `CHANGELOG.md` (Keep a Changelog) a `## [X.Y.Z] - <date>` section unless it
  has one: the `Unreleased` entries, or when there are none a draft with one line per Included PR, plus its link
  reference. That section, as it stands, becomes the config's `ChangeLog`. The `/promote` skill edits the section to
  what players notice and stamps again (an existing section is kept, so the edit is what gets copied), then commits
  it as the branch's one extra commit. `stamp` refuses a version below the declared one.
- That commit is on the promotion branch only, so the PR's head isn't in `autodev`'s history. The PR body records
  the `autodev` commit it was cut from (`Round head: <sha>`), and the next round starts there. The sync back after the
  merge brings the stamp into `autodev`.
- Nothing stamps an assembly version: the mod's csproj sets none. If one is added, stamp it too: `publish.ps1`
  refuses a csproj `Version` or `AssemblyVersion` other than `ModVersion`.

## tools/publish.ps1

`./tools/publish.ps1 [-Mode Publish|NewVersion|Update] [-Ref origin/master] [-DryRun] [-AccessLevel Private [-TestSuffix <label>]]`
(its help has every detail).

- **Where it builds.** It fetches, checks the ref out detached in a temporary worktree under TEMP (never the user's
  checkout), and runs `dotnet build -c Release` into a temporary Mods folder, as `build.ps1 -Zip` does. So the upload
  holds exactly the build: not what the game's Mods folder holds (`build-info.txt`, a branch someone deployed). The
  worktree and the folder are removed afterwards. It refuses while the game runs.
- **What it uploads.** The build's files (dll, mjs, `.pdb`, `.mjs.LICENSE.txt`, the Burst dll/so/bundle) minus the
  Burst debug symbols (`*_x86_64.pdb`, `$UploadExcludePatterns` in the script); the managed `.pdb` (line numbers in
  players' error logs) and the LICENSE.txt (third-party notices) stay. `-DryRun` prints the final list.
- **The mode follows `ModId`.** Empty: `Publish` (a new mod). Set: `NewVersion`. `-Mode Update` changes the page only
  (descriptions, images, tags, access level): nothing is built or tagged.
- **It refuses before uploading**, each refusal naming its fix: `ModVersion` other than `UI/mod.json`'s version; an
  empty `ChangeLog` for `NewVersion` or `Update`; a missing `Thumbnail` or `Screenshot` file; a `Thumbnail` or `Screenshot` over Paradox Mods' 2.1 MB image limit (2,100,000 bytes: Paradox refuses it only after the build is uploaded, "Image size should not exceed 2.1 MB"); a template leftover
  ("This is a long description", an empty `Tag`, `ExternalLink`, `Screenshot`, `Dependency` or `RequiredDLC`); an
  `AccessLevel` other than `Public`, `Private` or `Unlisted`; a `Screenshot` whose path is the one the last released
  `v*` tag has but whose bytes changed (it needs a new name: [Screenshots](#screenshots)); for a release, a
  `v<version>` tag that already exists; for a private test, a mod that has had a release. A refusal on `master` is
  fixed on `autodev` and promoted again.
- **`-DryRun`** does all of that and the Release build, then prints the exact command and stops.
- **After a successful release** (not a private test) it tags the built commit `v<version>` (an annotated tag naming
  the mode and access level) and pushes the tag. A failed build or upload tags nothing.

## Screenshots

The page's pictures live in `Mod/Properties/Screenshots/`, each listed as a `Screenshot` in
`PublishConfiguration.xml` in the order the mod page shows them; the README embeds the same files. The set is nine
shots, by base name (the number is when a shot was added, not its place on the page): `01-budget-chart`,
`02-budget-tooltip`, `03-taxation-density`, `04-options`, `05-budget-vanilla-detail`, `06-budget-district`,
`07-budget-district-list`, `08-services-table` (the table, Electricity hovered so the info column shows),
`09-taxation-district` (Taxation in the first district's view). A new shot gets a `<Screenshot>` entry and a README
link by hand (any version suffix): the install renames them.

- **Spec.** 1920x1080 24-bit RGB PNGs (about 1.2 to 1.4 MB each; the captures are opaque, and the 32-bit ARGB a `System.Drawing` bitmap saves by default put some over the 2.1 MB limit) from full-scale window captures at 2560x1440: an Economy panel shot is
  cropped to the 16:9 box (285, 193)-(2275, 1312) around the panel and scaled, the Options page is scaled whole.
  Taken on a paused save that is never saved (Odiham: districts and a year of history); a feature switch turned on
  for a shot (density taxes) goes back as it was.
- **Every Budget-tab shot has all the lines on**: every row in the left column toggled on, not just the two totals
  the tab starts with.
- **New names for new pictures.** Paradox Mods caches page images by name, so a retaken picture under an old name
  keeps showing the old one. Each set carries a version suffix: `01-budget-chart-v1-2-0.png`. The version is
  `ModVersion`, or, when a `v*` tag at or above it says that one is already released, the next patch after the
  highest tag (the version the next promotion will usually stamp). `publish.ps1` refuses a changed picture under a
  released name. The tags must be in the checkout that runs it: a checkout fed by a git bundle (the Windows PC from
  Linux) has none, so pass `-Version` there; a minor release the user picks (1.3.0) is also `-Version`, and
  `-FromDir <run>\raw -Version X.Y.Z` renames a set already taken without the game.
- **`tools/store-screenshots.ps1`** takes the set (`Get-Help ./tools/store-screenshots.ps1 -Full`): the same claim,
  launch, paused check and quit as `in-game-check.ps1`; before the launch it sets the mod's settings file
  (`FiscalOverhaul.coc` in the game's LocalLow folder: the chart, district recording, the services table, simulation
  changes and density taxes on, the density offsets template from `-Offsets`, which only a save without its own
  density rates takes: Odiham's own rates are what `03` shows) and puts the original back byte for byte after the game
  exits; it turns every Budget row on (reading each check square off a full-scale capture, so a row already on isn't
  toggled off), takes the Budget shots, **runs the simulation `-SimSeconds` (default 200) and pauses it again** before
  the Taxation shots (their estimates count only payers the tax engine charged lately: a save that loads paused has
  charged none, so without the run every estimate reads ¢0, and a full round of charges takes a little over 2
  minutes at 1x; shorter runs are scaled up from the groups charged so far, [tax-engine-ui.md](impl/tax-engine-ui.md#density-bindings-and-the-estimate)), takes the rest of the nine shots, switches density taxes back off on the Options page, quits, then crops, scales and installs
  them: the new files in `Screenshots/`, the `Screenshot` entries and README links renamed in place (the page order
  kept), the old files of each base removed. The whole set or nothing. Commit the result through a PR into `autodev`.
- **Points it hasn't clicked before** (`-ListPoints`: source `derived`) each get a check shot in `-CheckDir`; a run
  that misses one is redone with `-Point Name=x,y`, and the corrected value goes into `tools/game-run-common.ps1`.
  `-FromDir <dir>` re-crops and installs captures already taken (`<base>.png` at 2560x1440) without the game.
- **Discord's overlay** (its voice list at the window's left edge) shows in `04-options`, the one whole-window shot:
  the script warns when Discord runs. Turn the overlay off, or retake that shot.

## Private test uploads

`-AccessLevel Private` is a test of the pipeline or a build, and never uses up the release:

- It uploads from a temporary copy of the config with `AccessLevel` `Private` and `ModVersion`
  `<version>-<TestSuffix>`, by default `test.<MMddHHmm>` (`1.0.0-test.11042130`). Paradox refuses a new version whose
  version string equals the mod's current one, so each test needs its own; `-TestSuffix test.2` names one (letters,
  digits, dots, hyphens, at most 20).
- It tags nothing and ignores an existing `v<version>` tag, so `<version>` stays free for the release.
- It is refused once the mod has had a release (`ModId` set and any `v*` tag): the access level belongs to the whole
  mod, not a version, so a private upload would hide the public mod.
- A test of a new mod still creates the mod: record its `ModId` as for any `Publish` (below).

**The first public release after a private test** is then an ordinary release from the promoted `master`: `ModId` is
set, so the mode is `NewVersion`, with the plain version (`1.0.0`, different from the test's string) and the
config's `AccessLevel` `Public`. ModPublisher sends the access level with every request (`NewVersion` and `Publish`
as well as `Update`; read from its code and the SDK's `FillPublishMetadata`), so that one upload replaces the test
build and switches the page to Public; no separate `-Mode Update` is needed. Check the page afterwards: if it still
reads Private, `./tools/publish.ps1 -Mode Update` sets it again from the config.

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
