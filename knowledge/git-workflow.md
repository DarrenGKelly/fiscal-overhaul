---
type: Workflow
title: Git workflow
description: "How work gets from a chat into master: branch naming, PRs and versioning, autodev and landing, worktrees, test batches, promotion, the hooks and the Claude Code merge guard."
resource: .githooks
---

# Git workflow

How work gets from a chat into `master`, directly or (for autonomous sessions) through `autodev`. Enforced locally by
the tracked hooks in `.githooks/` (enable once per clone: `git config core.hooksPath .githooks`). Nothing is enforced
server-side, so the hooks and the merge guard are the enforcement.

## Rules

- **`master` only changes through pull requests.** The hooks refuse every commit on `master` and every push to it.
- **Every work branch is `feature/<name>` or `fix/<name>`**, `<name>` in kebab-case (`feature/chart-filters`,
  `fix/tax-rounding`). The hooks refuse any other name. The one shared exception is `autodev`.
- One task per branch, one chat per branch. Parallel chats each use their own worktree.
- **A problem found outside the task becomes a GitHub issue**, not a fix in passing ([issues.md](issues.md)).
- **PRs into `master` have version-titled titles**: `vX.Y.Z - <summary>`. The repo squash-merges and uses the PR title
  and body as the commit. PRs into `autodev` aren't versioned (parallel sessions would claim the same number).
- **`autodev` is the integration branch.** Autonomous sessions land their own PRs into it; the user tests it in game
  and promotes it to `master`. Anyone may commit to and push `autodev`, but the hooks refuse deleting it or any push
  that isn't a fast-forward. Claude only ever merges into `autodev`, and only through `tools/land-pr.ps1`.

## Lifecycle

1. Start from an up-to-date base: `git fetch --prune`, then `git switch -c feature/<name> --no-track origin/master`
   (or `origin/autodev` for autonomous work), or a worktree.
2. Work and commit. Run the build and tests ([testing.md § Running tests](testing.md#running-tests)); a UI change
   also needs its in-game screenshot (`/playtest`).
3. `git push -u origin feature/<name>`, then `gh pr create --base master --title "vX.Y.Z - …" --body-file <file>`
   (the next patch after `master`'s latest, `git log -1 origin/master --format=%s`). The user may call a minor bump;
   Claude only suggests one.
4. The user reviews and squash-merges. Then `git branch -D feature/<name>` (`-D`: git doesn't see a squash as merged).

## Autonomous sessions (autodev)

A session is autonomous when the user says so ("work autonomously", "on autodev"). It works without asking for
approval; the user reviews by testing `autodev` in game, not PR by PR. Start one with the global `/autodev` skill,
before any branching or code. What it needs from this project:

1. **Sync first.** `git fetch --prune`. If `git merge-base --is-ancestor origin/master origin/autodev` fails,
   [sync autodev](#syncing-autodev-with-master) before branching.
2. **Branch from `autodev`:** `git worktree add --no-track .claude/worktrees/<name> -b feature/<name> origin/autodev`,
   or in a worktree a tool created, `git switch -c feature/<name> --no-track origin/autodev`.
3. **Fast tier as you work:** `./tools/build.ps1 -NoDeploy` and `./tools/test.ps1`. **Slow tier once at the end:**
   deploy and check in game through `/playtest`, under [the one-game rule](orchestration.md#the-shared-game-instance).
   Never land with failing tests.
4. **Catch up:** `git fetch`, `git merge origin/autodev`; re-run the tests if anything conflicted or the merge touched
   what you changed.
5. `git push -u origin feature/<name>`, then `gh pr create --base autodev --title "<summary>" --body-file <file>`:
   what changed, what to try in game, the tests run, one `Fixes #<n>` per issue it closes (`Part of #<n>` for a slice).
6. **Land it yourself:** `./tools/land-pr.ps1 <number>`. It refuses a base other than `autodev`, a PR that isn't open,
   unpushed commits or uncommitted changes, a conflicting PR, one whose merge with `origin/autodev` puts a doc over its
   [size budget](knowledge-format.md#size-budgets) (or that no working python can check: `-SkipBudget`, only when the
   user asks), and one that changes a `tools/` or `.githooks/` file whose tool test fails
   ([tool tests](testing.md#tool-tests)). Then it squash-merges at the head sha it checked, deletes the remote
   branch, and detaches this checkout and deletes the local branch. A refusal names its fix.
7. **Stop instead of guessing.** A needed decision, or tests failing for reasons outside the task: leave the PR open
   with the question in its body.

Never force-push, reset or delete `autodev` (`ALLOW_AUTODEV_REWRITE=1` only when the user asks).

### Syncing autodev with master

Needed after every promotion and any PR merged straight into `master`: squash merges don't record ancestry.

```
git fetch --prune
git switch --detach origin/autodev
git merge --no-edit origin/master
git push origin HEAD:autodev
git switch -
```

A conflict means the branches really diverged: stop and tell the user. A push refused as not a fast-forward: a PR
landed meanwhile, start again from `git fetch`.

### Promoting autodev to master (the user)

The [`/promote` skill](../.claude/skills/promote/SKILL.md) does the Claude side: it syncs, writes the PR body with
`tools/promote.py plan` (only this round's PRs, and every `Fixes #N` they carry, since a merge into `autodev` closes
nothing), opens the PR from a throwaway `feature/promote-vX-Y-Z` branch (never from `autodev`: the repo deletes a
merged PR's head), and after the user merges, syncs back and checks the issues closed (`tools/promote.py verify`).
Only the user merges into `master`.

## Worktrees

Parallel chats each work in `.claude/worktrees/<name>` (gitignored). A fresh worktree has no `UI/node_modules`:
`tools/build.ps1` and `tools/test.ps1` install it on first use. Remove one with `./tools/remove-worktree.ps1 -Path <p>`
(never `rm -rf`: it unlinks junctions first, and refuses a dirty tree); `./tools/prune-worktrees.ps1 -Apply` removes
every clean worktree whose branch has landed. Every worktree deploys to the same Mods folder, so what the game loads is
whichever deployed last (`./tools/check-batch-status.ps1` says which).

## Test batches

When several branches are ready at once, test them **together**: one throwaway branch that merges them all onto
`origin/autodev`, deployed and checked in game once.

```
./tools/new-test-batch.ps1 -Name <kebab> -Branch feature/a,fix/b,feature/c -Check
./tools/new-test-batch.ps1 -Name <kebab> -Blame <failing test file, source file or folder>
```

- It builds `feature/batch-<name>` in `.claude/worktrees/batch-<name>` from `origin/autodev`, merging each
  `origin/<branch>` in order. **A branch that conflicts is left out** (exit 1), and the summary names the branch it
  collided with. `-Check` then runs `tools/build.ps1 -NoDeploy` and `tools/test.ps1` on the combined tree, for
  conflicts git can't see. `-Blame` names the branch that changed each path.
- Re-running with the same name rebuilds from scratch; every build force-pushes the batch branch (`-NoPush` keeps it
  local).
- **Batch branches are test-only.** Never open a PR from one or merge one. After the batch passes, each branch lands
  on its own PR without re-testing, as long as it merges cleanly.
- Clean up once they've landed: `./tools/remove-worktree.ps1 -Path .claude/worktrees/batch-<name>`,
  `git branch -D feature/batch-<name>`, `git push origin --delete feature/batch-<name>`.

## Hooks

| Hook | Does |
|---|---|
| `pre-commit` | `check_commit_branch`: refuses `master` and any branch but `feature/<kebab>`, `fix/<kebab>` or `autodev` (a detached HEAD passes). When the commit stages `knowledge/`, `.claude/skills`, `.claude/agents`, `tools/` or `CLAUDE.md`, `check_staged_docs` (`doc-checks.sh`) runs `okf.py check`, `check-doc-links.py` and `check-skills.py` and refuses on errors; a size-budget error refuses only for a staged doc. |
| `pre-push` | `check_push_ref` per pushed ref: refuses pushes to `master`, non-conforming branch names, and deleting `autodev` or pushing it anything but a fast-forward. Tags and deleting other remote branches are allowed. |
| `pre-merge-commit` | Warn-only: `okf.py budget-tree` on the merged tree names any doc over its budget. Never blocks. |

- The policy lives in `.githooks/branch-policy.sh`, the doc checks in `doc-checks.sh`. After changing any hook, run
  `sh tools/test-git-hooks.sh`.
- `core.hooksPath` is shared by every worktree; `tools/git-common.ps1` warns when it is unset or points elsewhere.
- Overrides, only when the user asks: `ALLOW_MASTER_COMMIT=1`, `ALLOW_MASTER_PUSH=1`, `ALLOW_AUTODEV_REWRITE=1`,
  `ALLOW_DOC_ERRORS=1`. Branch names have no override: rename the branch.
- Run the doc checks by hand with `python tools/okf.py check`, `python tools/check-doc-links.py` and
  `python tools/check-skills.py`.

### Claude Code merge guard

Git hooks can't see merges made on GitHub. So the tracked `.claude/settings.json` runs `tools/claude-merge-guard.sh`
before every Bash and PowerShell tool call, in every session. It blocks:

- `gh pr merge` as a command, any base: land with `./tools/land-pr.ps1 <N>`; `master` PRs are merged by the user on
  GitHub.
- merges through `gh api` (`pulls/N/merge`, `/merges`, GraphQL merge mutations);
- skipping the git hooks: `--no-verify`, `git commit -n`, pointing `core.hooksPath` anywhere but `.githooks`;
- deleting, renaming or rewriting branches or the repository through `gh api` or `gh repo`;
- `gh pr`/`gh issue` `create` and `comment` without `--body-file` (or `-F`).

It **warns** (allows) when a command sets a hook override or runs `land-pr.ps1 -SkipBudget`: a session that sees the
warning without the user's ask stops. Text in quotes and heredocs is data, not a command. After changing the guard,
run `./tools/test-claude-merge-guard.ps1`.
