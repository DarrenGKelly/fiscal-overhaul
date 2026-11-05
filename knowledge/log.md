# Update log

## 2026-11-04

* **Creation**: releasing to Paradox Mods ([releasing.md](releasing.md)): `tools/publish.ps1` and its twin, the [/publish](../.claude/skills/publish/SKILL.md) skill, and `tools/promote.py stamp`, which writes the version and ChangeLog into the promotion branch. Promotion bodies now record their round head, since the stamp commit keeps the PR's head out of `autodev`.

## 2026-11-03

* **Creation**: The AI and dev tooling, ported from the Fleet Commander repo and adapted to a Cities: Skylines II mod: the git hooks and the Claude Code merge guard ([git-workflow.md](git-workflow.md#hooks)), the agents and skills ([skills.md](skills.md)), `tools/build.ps1` and `tools/test.ps1` ([testing.md](testing.md#running-tests)), the one-game rule ([orchestration.md](orchestration.md#the-shared-game-instance)), and this bundle ([knowledge-format.md](knowledge-format.md)).
* **First in-game test and fixes**: Budget row values summed by source, canvas sizes scaled by the 4K-based rem, the in-progress sample dropped ([impl/budget-chart.md](impl/budget-chart.md)); launching through Steam and the game's logs ([cs2-ui-modding.md](cs2-ui-modding.md#logs)), folded into [the in-game check](testing.md#the-in-game-check).
* **GitHub**: the repo `DarrenGKelly/fiscal-overhaul` set up with `master` (default) and `autodev`, the issue labels, and LF line endings for hooks ([git-workflow.md](git-workflow.md)).
* **/playtest and /build restored to their Fleet Commander jobs**: the port had turned /playtest into an in-game screenshot check (now [testing.md § The in-game check](testing.md#the-in-game-check)) and /build into a deploy of the current checkout. /playtest now files the user's playtest notes as issues; /build builds `origin/autodev` (or a ref) in `.claude/worktrees/build` and deploys or zips it (`tools/build.ps1 -Ref`, `-Zip`). `issues.py labels` now creates `needs-decision` and `tech-debt`.
* **Budget chart fixes from the second playtest**: the chart's "Now" matched the trailing-month average, not the row's live rate (now sample × 32); smaller chart text; x ticks and faint vertical grid lines at whole weeks/months/years; the hover info moved under the filter rows and the chart widened into the freed column ([impl/budget-chart.md](impl/budget-chart.md)).
* **Budget chart, third playtest**: "Now" is the live per-source value, so it matches the rows; the hint box removed; the item detail pops out over the chart; the y-axis margin fits its labels; the tooltip reads "/mo" on one line ([impl/budget-chart.md](impl/budget-chart.md)).
* **/build on Linux**: the game also runs on the Linux PC under Proton; `tools/build-linux.sh` builds a ref on the Windows PC over SSH (origin fed from a git bundle, since the Windows logins don't reach an SSH session) and installs the zip into the Proton Mods folder ([cs2-ui-modding.md § Building](cs2-ui-modding.md#building), [/build](../.claude/skills/build/SKILL.md)).
