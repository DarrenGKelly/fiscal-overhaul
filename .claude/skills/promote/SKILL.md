---
name: promote
description: Promote Fiscal Overhaul's autodev branch to master — sync, open the version-titled promotion PR (vX.Y.Z - summary) with this round's PRs and their Fixes lines, then after the user merges it, sync autodev back and check the issues closed. Use when the user says /promote, "promote autodev", "merge autodev into master", "release autodev", or asks for the promotion PR. Never merges into master.
---

# Promote autodev to master

The user tests `autodev` in game and promotes it to `master` once a round of work is ready. The skill's job is the
paperwork: a PR whose description says what the round contains and closes what it fixed, and a clean sync back.

The two mistakes that cost the most:
- **Listing `git log master..autodev`.** The repo squash-merges, so git thinks every past round is missing from
  `master`. A round is the commits since the **last promotion's round head**; `tools/promote.py` works that out.
- **Dropping the "Fixes #N" lines.** A "Fixes #N" in a PR merged into `autodev` closes nothing: GitHub only acts on
  merges into the default branch. The promotion PR's description repeats them.

Reference: [git-workflow.md § Promoting](../../../knowledge/git-workflow.md#promoting-autodev-to-master-the-user) and
[§ Syncing](../../../knowledge/git-workflow.md#syncing-autodev-with-master).

## 1. Sync autodev with master

```
git fetch --prune
git merge-base --is-ancestor origin/master origin/autodev || echo "needs sync"
```

When it needs a sync:
```
git switch --detach origin/autodev
git merge --no-edit origin/master
git push origin HEAD:autodev
git switch -
```
A conflict means the branches really diverged: stop and tell the user. A push refused as not a fast-forward means a
PR landed meanwhile: start again from `git fetch`.

## 2. Plan the round

```
python tools/promote.py plan --out <scratchpad>/promote-body.md
```

It prints whether `autodev` is synced, the last promotion PR (none before the first: then the round is everything
since `autodev` left `master`, version `v0.1.0`), the `autodev` head the round ends at, the next patch version (or
the higher `ModVersion` autodev already declares) and the round's size, and writes the PR description: the round
head, an Included list and one `Fixes #N` line per issue the listed PRs fix. Exit 1 means `autodev` isn't synced, or
the round is empty. Read the body before using it:
- The Included list should look like one round. One that runs back weeks means the last promotion wasn't found
  (`--since <PR>` names it).
- Never trim the Included list by hand: a PR left out loses its `Fixes` lines.
- Drop a `Fixes #N` only if that issue was deliberately left open; say so in the body.
- Quote the knowledge bundle's growth, measured on the refs: `python tools/okf.py growth origin/master origin/autodev`.

**Title:** `vX.Y.Z - <summary of this round> (autodev promotion)`, using the version the script printed. Summarise
only what's in the Included list. A minor bump is the user's call: suggest one when the round adds a feature players
will notice, but don't pick it yourself.

## 3. Stamp the release and open the promotion PR

Open it from a throwaway branch, never with `autodev` as the head (deleting the head on merge would delete `autodev`).
The branch is the round head plus one commit stamping the version and ChangeLog that `/publish` uploads
([releasing.md § The version and ChangeLog](../../../knowledge/releasing.md#the-version-and-changelog)):

```
git switch --detach <autodev head plan printed>
python tools/promote.py stamp vX.Y.Z --body <scratchpad>/promote-body.md
```

The ChangeLog it drafts (`<ChangeLog>` in `Mod/Properties/PublishConfiguration.xml`) lists every Included PR. Edit it
to what players will notice, in plain words, one `- ` line each; drop tooling and docs-only lines (keep at least one).
Then:

```
git commit -am "Stamp vX.Y.Z: ModVersion, UI/mod.json version and ChangeLog"
git push origin HEAD:refs/heads/feature/promote-vX-Y-Z
git switch -
gh pr create --base master --head feature/promote-vX-Y-Z --title "vX.Y.Z - … (autodev promotion)" --body-file <scratchpad>/promote-body.md
```

- The version is the title's: a minor bump the user chose is stamped as that version. `stamp` refuses one below the
  `ModVersion` already declared.
- Keep the body's `Round head` line: the stamp commit isn't in `autodev`, so the next round starts from that line.
- Name the branch `feature/promote-vX-Y-Z` exactly: `promote.py` finds the last promotion by that prefix (or the
  "(autodev promotion)" title).
- `--body-file` is required (the merge guard refuses `gh pr create` without it). To change the title or body later:
  `gh api -X PATCH repos/{owner}/{repo}/pulls/<N> -f title="…" -F body=@<file>` (gh fills in the owner and repo).

Then **stop and hand over**: the link, the round in a few lines, and the version. Only the user merges into `master`;
the merge guard blocks Claude, and the hooks block direct pushes. Don't merge it, and don't ask to.

## 4. After the user merges: sync back and verify

Sync `autodev` with the new `master` (step 1's commands). Then:

```
git fetch --prune
git diff --stat origin/master origin/autodev
python tools/promote.py verify <PR>
python tools/issues.py fixed
```

- The diff should be empty: the new round starts from the release.
- `verify` checks the PR merged into `master`, `autodev` contains `master`, and every issue fixed by a listed PR is
  closed. Close what it lists with `gh issue close <N> --reason completed --comment "Fixed on autodev and promoted to
  master in #<PR> (vX.Y.Z)."`, unless step 2 left it open on purpose.

Tell the user the sync is pushed, the branches match, and which issues closed.

## When something doesn't fit

- **The user ran part of it themselves**: check what exists (`git ls-remote origin 'refs/heads/feature/promote-*'`,
  `gh pr list --base master --state open --limit 20`) and continue from there. Don't open a second PR.
- **The permission check refuses a step**: stop, say what you were doing, and give the user the command.
- **Tests:** a promotion carries only code tested when it landed, so this skill runs none. After changing
  `tools/promote.py`, run `python tools/test-promote.py`.

Next: once the user has merged and the sync is done, `/publish` uploads the release to Paradox Mods.
