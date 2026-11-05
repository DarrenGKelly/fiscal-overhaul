---
name: publish
description: Publish a promoted Fiscal Overhaul release to Paradox Mods — check master is promoted and synced, dry-run tools/publish.ps1, show the user the version, ChangeLog and command, upload only on their yes, then confirm the v<version> tag and record a new mod's ModId. Use when the user says /publish, "publish the mod", "upload to Paradox Mods", "release it", or asks for a private test upload.
---

# Publish a release to Paradox Mods

The user runs this after `/promote`'s PR has merged into `master`. The skill's job: prove the release is ready with a
dry run, let the user decide, upload, and leave the repo recording what was published.

The mistakes that cost the most:
- **Uploading without the user's yes.** An upload is public (unless `-AccessLevel Private`) and can't be taken back
  by a script. Ask once, after the dry run, showing exactly what goes up.
- **Passing credentials.** ModPublisher logs in with the session the game stored. Never pass `-e`, `-p`,
  `--loginData` or `--noAutoLogin`, never ask for a password: a login failure means the user launches the game, logs
  in and closes it.
- **Fixing a refusal on `master`.** Every refusal is fixed on `autodev` (a PR) and promoted again; `master` changes
  only through promotions.
- **Leaving a new mod's ModId uncommitted.** Until it is in the config, the next run would publish a second mod.

Reference: [releasing.md](../../../knowledge/releasing.md) (the flow, every refusal, what ModPublisher does).

## 1. Check master is promoted and synced

```
git fetch --prune --tags
git log -1 --format=%s origin/master
git merge-base --is-ancestor origin/master origin/autodev || echo "autodev not synced"
```

- The subject should be the promotion, `vX.Y.Z - … (autodev promotion)`. Anything else: master isn't a fresh
  promotion; ask the user whether to `/promote` first.
- Not synced: `/promote` wasn't finished (its step 4 syncs back); finish it before publishing.
- The game must be closed (the script refuses otherwise). Never launch it yourself.

## 2. Dry run

```
./tools/publish.ps1 -DryRun
```

Add `-AccessLevel Private` for a test upload only the account can see, `-Mode Update` for a page-only change. Exit 2
is a refusal: relay each reason and its fix, and stop. Exit 1 is a failed build: show the error and stop.

## 3. Ask the user

Show, in a few lines, and ask whether to upload:
- the mode (`Publish` = a new mod, `NewVersion`, `Update`), the version, the access level, and the commit (`Ref`);
- the ChangeLog: `git show origin/master:Mod/Properties/PublishConfiguration.xml` (between `<ChangeLog>` tags);
- the command the dry run printed.

Only a clear yes in the chat goes ahead.

## 4. Upload

Run the same command without `-DryRun` (same `-AccessLevel` and `-Mode`):

```
./tools/publish.ps1
```

- Exit 0: it uploaded, and for `Publish`/`NewVersion` tagged the commit `vX.Y.Z` and pushed the tag.
- Exit 1 from ModPublisher: nothing was tagged. "Could not auto log in": the user launches the game, logs in, closes
  it, and you rerun step 4. Anything else: show the output and stop.
- Uploaded but the tag push failed: run the `git tag`/`git push` line the script printed.

## 5. Record it

- Check the tag: `git ls-remote --tags origin vX.Y.Z`.
- **A new mod (`Publish`)**: the script printed `New ModId: <n>`. Commit it at once, through the autodev flow
  ([git-workflow.md](../../../knowledge/git-workflow.md#autonomous-sessions-autodev)): a branch
  `feature/record-mod-id` from `origin/autodev`, `<ModId Value="<n>" />` in `Mod/Properties/PublishConfiguration.xml`,
  a PR into `autodev` (`gh pr create --base autodev --body-file <file>`), landed with `./tools/land-pr.ps1 <PR>`. It
  reaches `master` with the next promotion, whose release is then a `NewVersion` (a rerun before that is refused by
  the version's tag).
- Tell the user: what was uploaded (mode, version, access level), the tag, and the ModId PR if any. A private test
  upload goes public later with `./tools/publish.ps1 -Mode Update` once the config says `Public`.

## When something doesn't fit

- **The user already uploaded by hand** (Visual Studio's publish profiles): tag the commit yourself
  (`git tag -a vX.Y.Z <sha> -m "…"`, `git push origin vX.Y.Z`) and record the ModId as in step 5.
- **Tests:** after changing `tools/publish.ps1`, run `./tools/test-publish.ps1`.
