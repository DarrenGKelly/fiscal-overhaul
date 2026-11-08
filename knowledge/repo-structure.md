---
type: Reference
title: Repo structure
description: Where everything lives — the C# mod, the UI module, the tests, the knowledge bundle, the git hooks and every script under tools/ with what it does and how it is tested.
resource:
  - tools
---

# Repo structure

`tools/okf.py check` fails when a top-level folder of `Mod/`, `UI/src/` or `Tests/` isn't named here, and
`tools/check-doc-links.py` fails when a folder a table names doesn't exist or a `tools/` file isn't named here (or in
a skill, agent or hook). Build output (`bin/`, `obj/`, `node_modules/`, `UI/build/`) is gitignored.

## Top level

| Folder | What |
|---|---|
| `Mod/` | The C# mod (`FiscalOverhaul.csproj`, net48, built with the game's csiimod toolchain): `Mod.cs`, `Setting.cs`, `LocaleRegistry.cs` (registers `l10n/`) |
| `UI/` | The UI module (TypeScript, React, SCSS; the CS2 UI template's webpack build): `src/`, `types/`, `webpack.config.js` |
| `l10n/` | The mod's text, one `<locale>.json` per language (en-US only so far): embedded in the DLL for the game, bundled into the UI as the English fallback ([cs2-ui-modding.md § Localization](cs2-ui-modding.md#localization)) |
| `Tests/` | xunit tests for the mod's pure logic (net10.0; compiles `Mod/Logic/` in directly, no game DLLs) |
| `knowledge/` | This OKF bundle ([knowledge-format.md](knowledge-format.md)) |
| `art/` | Source art: `icon/` renders the mod icon `Mod/Properties/Thumbnail.png` (1024x1024, Paradox Mods covers are square) and alternative concepts with `make_icon.py` (Pillow), lettered in the game's UI font Overpass Bold (`icon/fonts/`, with its SIL OFL licence); its `README.md` says how |
| `tasks/` | `/orchestrate` batch folders (`tasks/<batch>/`, gitignored; `.gitkeep` keeps the folder) |
| `tools/` | Scripts (below) |
| `.githooks/` | The branch policy and doc checks ([git-workflow.md § Hooks](git-workflow.md#hooks)) |
| `.claude/` | `settings.json` (the merge guard), `agents/`, `skills/` ([skills.md](skills.md)) |

Root files: `README.md` (the public page players and contributors read, which embeds the Paradox Mods
screenshots), `LICENSE` (MIT), `CHANGELOG.md` (Keep a Changelog; one entry per release) and `CLAUDE.md`.

## Mod/

| Folder | What |
|---|---|
| `Logic/` | Pure logic with no game or Unity types (budget history maths, the density tax table): unit-tested in `Tests/` |
| `Systems/` | The game systems: ECS systems that read the city's statistics, apply density taxes, and the UI systems that bind data to the panel ([impl/](impl/index.md)) |
| `Properties/` | `PublishConfiguration.xml` (the Paradox Mods page: name, descriptions, tags, links, `ModVersion` and the release's `ChangeLog`), `Thumbnail.png`, `PublishProfiles/`. The version is in step with `UI/mod.json` and `CHANGELOG.md` |

## `UI/src/`

| Folder | What |
|---|---|
| `budget/` | The replacement Budget tab page |
| `district/` | The district selector and the one shared selected district, used by the Budget, Taxation and Services tabs |
| `common/` | Pieces shared by the tabs: a slider row's reset button, target marker and their frame, with their pure logic (`slider-reset.ts`, tested) |
| `chart/` | The line chart component and its pure maths (`chart-math.ts`, tested by vitest) |
| `services/` | The Services tab's per-district budget (`service-page.tsx`) and its pure logic (`district-services.ts`, tested) |
| `taxation/` | The density rows on the Taxation tab and their pure logic (`density.ts`, tested) |
| `economy/` | Economy panel tab order (`tab-order.ts`, tested) |
| `l10n.ts` | `localize()`: a UI string by key through the game's translate, English fallback (tested) |

## Tests/

No subfolders yet: one `*Tests.cs` file per `Mod/Logic/` class.

## tools/

Each script's own help (`Get-Help ./tools/<name>.ps1 -Full`, or the docstring) is the full reference. Every script
has a `tools/test-<name>` twin (or a reason it has none in `NO_TEST_TWIN` in `check-doc-links.py`): run it after
changing the script ([testing.md § Tool tests](testing.md#tool-tests)).

| Script | Does |
|---|---|
| `build.ps1` | Builds and deploys the mod: `-ModOnly`, `-UIOnly`, `-NoDeploy` (compile check), `-Configuration`; `-Ref origin/<branch>` builds that ref in `.claude/worktrees/build`; `-Zip` packages to `Builds/` without deploying. Refuses to deploy while the game runs. No test twin (needs the game's toolchain; verified by real runs) |
| `build-linux.sh` | On the Linux PC: builds a ref on the Windows PC over SSH (`build.ps1 -Ref <ref> -Zip`, origin fed from a git bundle made here) and installs the zip into the game's Proton Mods folder; `--ref`, `--configuration`. Refuses while the game runs. Tested by `test-build-linux.sh` |
| `publish.ps1` | Publishes a release to Paradox Mods through the game's ModPublisher (`/publish`): builds a ref (default `origin/master`) in a temporary worktree, refuses an unready config, uploads (`-Mode Publish`, `NewVersion` or `Update`; the default follows ModId) and tags `v<version>`; `-DryRun`; `-AccessLevel Private` is an untagged test upload (`<version>-test.<n>`, `-TestSuffix`). Refuses while the game runs ([releasing.md](releasing.md)). Tested by `test-publish.ps1` |
| `test.ps1` | Runs the unit tests: xunit (`dotnet test Tests`) and vitest (`npm test` in `UI/`); `-CSharpOnly`, `-UIOnly`, `-Filter`. Tested by `test-test.ps1` |
| `git-common.ps1` | Helpers the PowerShell tools dot-source: paths, `Invoke-Git`, the repo context, the hooks-path warning, `Use-LocalToolchain` (the per-user Node and .NET 6), `Get-GameProcess` |
| `land-pr.ps1` | Lands a PR into `autodev` ([git-workflow.md](git-workflow.md#autonomous-sessions-autodev)) |
| `new-test-batch.ps1` | Builds a test batch branch from several branches; `-Check`, `-Blame` ([git-workflow.md § Test batches](git-workflow.md#test-batches)) |
| `game-control.ps1` | Drives the game from the shell on Windows, no computer use ([testing.md § On Windows without computer use](testing.md#on-windows-without-computer-use)): `screenshot` (primary monitor, `-Region`, `-Window`, `-Scale`, `-Delay`), `click`, `move`, `move-relative`, `hover`, `key` (scan codes), `focus`/`window` (the game, or `-Title`), `windows`, `wait-process` (`-Title`, `-Gone`), `wait-loaded` (the save), `cursor`. Tested by `test-game-control.ps1` (no game needed; not in `$LandingPowerShellSuites`, as it moves the cursor) |
| `in-game-check.ps1` | The in-game check as one script ([testing.md § The in-game check](testing.md#the-in-game-check)): refuses unless the game is free, deploys (`-SkipDeploy`, `-Ref`), launches through Steam, resumes the last save, keeps it paused, screenshots Budget, the chart tooltip and Taxation into `-Out` with a `manifest.md`, runs `-Steps` (a file of extra clicks, hovers, keys and shots), and always quits the instance it launched. `-Point Name=x,y` corrects a point, `-ListPoints`, `-DryRun`. Tested by `test-in-game-check.ps1` |
| `store-screenshots.ps1` | The Paradox Mods screenshot set ([releasing.md § Screenshots](releasing.md#screenshots)): the same claim, launch and quit; sets the mod's settings file for the shots (density taxes on, `-Offsets`) and restores it; turns every Budget row on; takes the seven shots, crops and scales them to 1920x1080 and installs them as `<base>-v<X-Y-Z>.png`, renaming the `Screenshot` entries and README links and removing the old files. `-FromDir` re-crops captures already taken; `-Version`, `-DryRun`. Tested by `test-store-screenshots.ps1` |
| `game-run-common.ps1` | What those two dot-source: the named points (client offsets in the 2560x1440 window, measured or derived), the claim, launch, paused check and quit, the steps grammar, the crop, the store set's names and the config, README and settings rewrites |
| `check-batch-status.ps1` | One sweep for an orchestrator: each branch against `origin/autodev`, worktree state, PRs, whether the game is running, and what the Mods folder holds |
| `prune-worktrees.ps1` | Removes worktrees whose branch has landed and that are clean (`-Apply`; dry run without) |
| `remove-worktree.ps1` | Removes one worktree safely: unlinks junctions first, refuses a dirty one without `-Force` |
| `claude-merge-guard.sh`, `claude-merge-guard.ps1` | The Claude Code PreToolUse hook ([git-workflow.md § Hooks](git-workflow.md#hooks)); the `.sh` pre-filters, the `.ps1` decides. Tested by `test-claude-merge-guard.ps1` |
| `okf.py` | The knowledge bundle's checker and index generator ([knowledge-format.md](knowledge-format.md)) |
| `check-doc-links.py` | Broken links and anchors in the docs, skills and agents; the orphan-tool, test-twin and folder-map guards |
| `check-skills.py` | Skill and agent frontmatter, sizes, and that every `tools/` script and flag they name exists ([skills.md](skills.md)) |
| `issues.py` | The issue backlog by area, triage, and `/orchestrate` batches from it ([issues.md](issues.md)) |
| `promote.py` | Plans, stamps (`ModVersion`, `UI/mod.json`'s version, the `CHANGELOG.md` section and the config's `ChangeLog`) and verifies an `autodev` → `master` promotion (`/promote`; [releasing.md](releasing.md#the-version-and-changelog)) |
| `test-git-hooks.sh` | The hooks' tests (no twin of its own: it is the `.githooks/` test) |
