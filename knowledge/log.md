# Update log

## 2026-11-07

* **Tax happiness**: citizens' tax happiness follows their home's charged rate (density + district adjustments) through `TaxHappinessSystem`, a copy of `CitizenHappinessSystem`'s update that runs exactly while the tax engine does ([impl/tax-engine.md § Happiness](impl/tax-engine.md#happiness)).

## 2026-11-06

* **Scripted game runs**: `tools/in-game-check.ps1` runs the in-game check end to end (claim, deploy, Steam launch, paused check, Budget/tooltip/Taxation shots, `-Steps`, a manifest, always quit) and `tools/store-screenshots.ps1` takes and installs the Paradox Mods set, both on `tools/game-run-common.ps1`. Two new screenshot rules: every Budget-tab shot has all lines on, and each set gets new, version-suffixed names, which `publish.ps1` now enforces (a changed picture under a released name is refused) ([releasing.md § Screenshots](releasing.md#screenshots)).
* **v1.2.0 upload refused for image size**: Paradox Mods rejected two of the new screenshots (2.15 and 2.36 MB) as over its 2.1 MB image limit, after the build had gone up. `Save-CroppedImage` now writes 24-bit RGB PNGs (the captures are opaque; the 32-bit ARGB default was a third bigger), the set was re-encoded pixel for pixel (1.17 to 1.40 MB), and `publish.ps1` refuses an image over 2,100,000 bytes before uploading ([releasing.md](releasing.md#toolspublishps1)).

## 2026-11-05

* **Localization** (#12): every player-facing string is a key in `l10n/<locale>.json`, which the C# side embeds and registers per locale and the UI reads through the game's translate with English fallback; vanilla keys reused where the game has them; adding a language is one file ([cs2-ui-modding.md § Localization](cs2-ui-modding.md#localization)).
* **Tool fixes #10, #11**: `tools/remove-worktree.ps1` runs git against the worktree's owning repo (`-C`), so it works from any folder and `publish.ps1` no longer needs its Push-Location; webpack bumped to 5.112.2 so `npm ci` no longer prints ERESOLVE peer-dependency overrides.
* **The in-game check without computer use**: `tools/game-control.ps1` (and its twin) drives the game from the shell on Windows (screenshots, clicks, hover, scan-code keys, window focus, waiting for the save to load); proven in two runs on the Odiham save, and now the test agent's method ([testing.md § On Windows without computer use](testing.md#on-windows-without-computer-use)). The Linux PC's xdotool note joined it.

## 2026-11-04

* **Creation**: releasing to Paradox Mods ([releasing.md](releasing.md)): `tools/publish.ps1` and its twin, the [/publish](../.claude/skills/publish/SKILL.md) skill, and `tools/promote.py stamp`, which writes the version, the `CHANGELOG.md` section and the ChangeLog into the promotion branch. Promotion bodies now record their round head, since the stamp commit keeps the PR's head out of `autodev`. A private test upload (`-AccessLevel Private`) uploads `<version>-test.<n>` and tags nothing, so the release stays unused ([releasing.md § Private test uploads](releasing.md#private-test-uploads)).
* **First Paradox Mods upload**: a Private test upload (ModId 166297, version 1.0.0-test.1) proved tools/publish.ps1; the ModId is recorded in the publish config ([releasing.md](releasing.md)).

## 2026-11-03

* **Creation**: The AI and dev tooling, ported from the Fleet Commander repo and adapted to a Cities: Skylines II mod: the git hooks and the Claude Code merge guard ([git-workflow.md](git-workflow.md#hooks)), the agents and skills ([skills.md](skills.md)), `tools/build.ps1` and `tools/test.ps1` ([testing.md](testing.md#running-tests)), the one-game rule ([orchestration.md](orchestration.md#the-shared-game-instance)), and this bundle ([knowledge-format.md](knowledge-format.md)).
* **First in-game test and fixes**: Budget row values summed by source, canvas sizes scaled by the 4K-based rem, the in-progress sample dropped ([impl/budget-chart.md](impl/budget-chart.md)); launching through Steam and the game's logs ([cs2-ui-modding.md](cs2-ui-modding.md#logs)), folded into [the in-game check](testing.md#the-in-game-check).
* **GitHub**: the repo `DarrenGKelly/fiscal-overhaul` set up with `master` (default) and `autodev`, the issue labels, and LF line endings for hooks ([git-workflow.md](git-workflow.md)).
* **/playtest and /build restored to their Fleet Commander jobs**: the port had turned /playtest into an in-game screenshot check (now [testing.md § The in-game check](testing.md#the-in-game-check)) and /build into a deploy of the current checkout. /playtest now files the user's playtest notes as issues; /build builds `origin/autodev` (or a ref) in `.claude/worktrees/build` and deploys or zips it (`tools/build.ps1 -Ref`, `-Zip`). `issues.py labels` now creates `needs-decision` and `tech-debt`.
* **Budget chart fixes from the second playtest**: the chart's "Now" matched the trailing-month average, not the row's live rate (now sample × 32); smaller chart text; x ticks and faint vertical grid lines at whole weeks/months/years; the hover info moved under the filter rows and the chart widened into the freed column ([impl/budget-chart.md](impl/budget-chart.md)).
* **Budget chart, third playtest**: "Now" is the live per-source value, so it matches the rows; the hint box removed; the item detail pops out over the chart; the y-axis margin fits its labels; the tooltip reads "/mo" on one line ([impl/budget-chart.md](impl/budget-chart.md)).
* **Tooltip "/mo" on one line**: the value is now one string; Gameface wraps between JSX text nodes even under `nowrap` ([cs2-ui-modding.md § Gameface](cs2-ui-modding.md#gameface-the-games-browser-limits)).
* **/build on Linux**: the game also runs on the Linux PC under Proton; `tools/build-linux.sh` builds a ref on the Windows PC over SSH (origin fed from a git bundle, since the Windows logins don't reach an SSH session) and installs the zip into the Proton Mods folder ([cs2-ui-modding.md § Building](cs2-ui-modding.md#building), [/build](../.claude/skills/build/SKILL.md)).
