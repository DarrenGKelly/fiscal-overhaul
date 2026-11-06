---
type: Workflow
title: Orchestration
description: What the global /orchestrate and /autodev skills need from this project — the agent types, the one shared game instance and how it is scheduled, briefs and model tiers, the test plan commands, and the batch rules.
resource:
  - .claude/agents
---

# Orchestration

The `/orchestrate` and `/autodev` skills are global (`~/.claude/skills/`); they read this doc, [git-workflow.md](git-workflow.md),
[testing.md](testing.md) and [issues.md](issues.md) for the project's specifics. A project rule here wins over the
skill. In short: branches `feature/<kebab>` / `fix/<kebab>` off `origin/autodev`, landed with `./tools/land-pr.ps1`;
the fast tier is `./tools/build.ps1 -NoDeploy` plus `./tools/test.ps1`; the slow tier is a deploy and an in-game check,
and the game is **the one shared resource**.

## The shared game instance

Cities: Skylines II (Steam app 949230, process `Cities2.exe`) runs once per machine, and every deploy goes to the same
Mods folder (`%CSII_LOCALMODSPATH%\FiscalOverhaul`), which the running game locks. So:

- **Only the orchestrator schedules the game**, and **one test agent holds it at a time**. Children never deploy or
  launch it; their fast tier (`build.ps1 -NoDeploy`, `test.ps1`) runs in parallel freely.
- **Taking it:** `./tools/check-batch-status.ps1` must say `Game: not running`; then the holder deploys
  (`./tools/build.ps1`, which refuses while the game runs) and launches. There is no lock file: the running process is
  the lock. Running when you didn't launch it means the user or another agent has it: wait or ask, never close it.
- **Releasing it:** the holder quits the game from its menu and confirms `Game: not running`, every time, red or green.
- **What it loads:** whatever deployed last, from any worktree. `check-batch-status.ps1` prints `Deployed mod:` from the
  `build-info.txt` `build.ps1` writes (branch, commit, time); the test agent confirms it names the sha under test.
- The user playing counts as holding it.

## Agent types

`.claude/agents/` defines the three kinds of session a batch spawns, as `subagent_type` on the `Agent` tool. Each fixes
its model and carries its standing rules, so the orchestrator's prompt carries only the spawn's specifics.

| Agent | Model | For |
|---|---|---|
| [researcher](../.claude/agents/researcher.md) | Sonnet, read-only | Recall questions: where X happens, which files each brief touches. **One per batch** for the contention map. |
| [batch-child](../.claude/agents/batch-child.md) | Opus; Haiku or Sonnet by `model` for a spelled-out brief | Implementing a brief, a bundle of small briefs, or a fix on an existing branch. Runs the fast tier itself; never deploys. |
| [test-agent](../.claude/agents/test-agent.md) | Sonnet, no file edits | Running a test plan: unit tests, deploy, the in-game check driven from the shell (`tools/game-control.ps1`, no computer use), a short report. Holds the game while it runs. |

`python tools/check-skills.py` lints them: frontmatter, a `model`, links, and every `tools/` flag they name.

## Briefs and model tiers

| Tier | Spawn `batch-child` with | For |
|---|---|---|
| Haiku | `model: "haiku"` | A spelled-out brief that changes only pure logic, tests and docs: most `bug`/`tech-debt` issues naming `file:line` and a fix |
| Sonnet | `model: "sonnet"` | A spelled-out brief that also touches a game system or a UI component |
| Opus | no `model` | Features, unknown root causes, anything hooking new game internals or vanilla UI, and **every failure of a Haiku or Sonnet child** |

A **spelled-out brief** is the issue text plus these lines, written from the code (`/plan-feature` writes them for a
feature's slices). One line you'd have to guess means Opus.

```
Tier:       spelled-out (haiku | sonnet)
Change:     <file:line — what changes there>              (one per site)
Tests:      <test file — name — pass case / fail case>    (one per behaviour)
In-game:    <what the test agent screenshots, or none>
Docs:       <knowledge/ section the change makes stale, or none>
Off-limits: <files it must not touch>
```

## Test plan commands

The "Run" lines of the test agent's prompt, from the owner worktree (the batch's or the branch's):

| Plan | Run |
|---|---|
| Test batch (`.claude/worktrees/batch-<name>`) | `./tools/new-test-batch.ps1 -Name <name> -Branch <b1>,<b2> -Check` first (the orchestrator, no game); then the test agent: `./tools/test.ps1`, `./tools/build.ps1`, then [the in-game check](testing.md#the-in-game-check) with every branch's `In-game` steps in one game session |
| Per-branch plan | the same from the branch's worktree |
| Unit tests only (no `In-game` steps) | `./tools/test.ps1`: any session, no game needed, no test agent needed |

A failing test names its file; `./tools/new-test-batch.ps1 -Name <name> -Blame <file>` names the branch that changed it.

## Batch rules

- **Spawn every child with `isolation: "worktree"`**, a fix on an existing branch too.
- **Land each PR with `./tools/land-pr.ps1 <number>`**, as its own command
  ([what it checks](git-workflow.md#autonomous-sessions-autodev)). The merge guard blocks a direct `gh pr merge`.
- **Sweep state with `./tools/check-batch-status.ps1 -Fetch -Branch <b>,...`** between merges: who fell behind, which
  worktrees are dirty, whether the game is running and what it loads.
- **Clean up with `./tools/prune-worktrees.ps1 -Apply`** once the PRs have landed (dry run without `-Apply`).
- **Batch in-game checks**: one deploy and one game session for every ready branch, through a test batch, rather than
  a launch per branch (a launch and save load is the slow part).
- **Resolve generated `index.md`/`log.md` conflicts by regenerating**: keep both log bullets, run
  `python tools/okf.py index`.
- **Verify a child's claims before acting**: the PR state, the pushed sha, the build-info the test agent read.
- **The user says to stop**: relay it to the test agent, which quits the game and reports.
- **Hand the user one command per block, with no `&&`** (Windows PowerShell 5.1 can't parse it).
