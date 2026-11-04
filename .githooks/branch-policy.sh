# Branch policy shared by pre-commit and pre-push (sourced, not a hook). Tested by tools/test-git-hooks.sh.
# Work branches must be feature/<name> or fix/<name> with a kebab-case <name>; master only changes through pull requests.
# autodev is the integration branch autonomous sessions merge into: it can be committed to and pushed,
# but never deleted or rewritten, because other sessions' merged work lives only there.

BRANCH_POLICY_HINT="Branches must be named feature/<name> or fix/<name>, <name> in kebab-case (lowercase words and digits joined by single hyphens, e.g. feature/chart-filters). Rename with: git branch -m feature/<name>"

# work_branch_name <branch>: succeeds for feature/<kebab-name> or fix/<kebab-name>.
work_branch_name() {
    case "$1" in
        feature/?*) wbn_name=${1#feature/} ;;
        fix/?*) wbn_name=${1#fix/} ;;
        *) return 1 ;;
    esac
    printf '%s\n' "$wbn_name" | grep -Eq '^[a-z0-9]+(-[a-z0-9]+)*$'
}

# check_commit_branch <branch>. The branch is empty on a detached HEAD (rebase, bisect), which is allowed.
check_commit_branch() {
    case "$1" in
        "")
            return 0 ;;
        master)
            [ "$ALLOW_MASTER_COMMIT" = "1" ] && return 0
            echo "pre-commit: don't commit on master. Create a branch (git switch -c feature/<name>) and open a PR." >&2
            echo "            Emergency override: ALLOW_MASTER_COMMIT=1 git commit ..." >&2
            return 1 ;;
        autodev)
            return 0 ;;
        *)
            work_branch_name "$1" && return 0
            echo "pre-commit: '$1' isn't an allowed branch name. $BRANCH_POLICY_HINT" >&2
            return 1 ;;
    esac
}

# staged_paths: the paths in the commit being made, one per line, unquoted.
staged_paths() {
    git -c core.quotePath=false diff --cached --name-only --no-renames
}

# check_push_ref <remote_ref> <local_sha> <remote_sha>. An all-zero local sha means the remote ref is being
# deleted; an all-zero remote sha means it's being created.
check_push_ref() {
    if [ "$1" = "refs/heads/master" ]; then
        [ "$ALLOW_MASTER_PUSH" = "1" ] && return 0
        echo "pre-push: master only changes through pull requests. Push a branch and run 'gh pr create'." >&2
        echo "          Emergency override: ALLOW_MASTER_PUSH=1 git push ..." >&2
        return 1
    fi

    if [ "$1" = "refs/heads/autodev" ]; then
        check_autodev_push "$2" "$3"
        return $?
    fi

    # Deleting any other remote branch (including old, non-conforming ones) is fine.
    case "$2" in
        *[!0]*) ;;
        *) return 0 ;;
    esac

    case "$1" in
        refs/tags/*)
            return 0 ;;
        *)
            work_branch_name "${1#refs/heads/}" && return 0
            echo "pre-push: '${1#refs/heads/}' isn't an allowed branch name. $BRANCH_POLICY_HINT" >&2
            return 1 ;;
    esac
}

# check_autodev_push <local_sha> <remote_sha>. Creating autodev is fine; deleting it or a non-fast-forward is not.
check_autodev_push() {
    [ "$ALLOW_AUTODEV_REWRITE" = "1" ] && return 0

    case "$1" in
        *[!0]*) ;;
        *)
            echo "pre-push: don't delete autodev; autonomous sessions merge into it." >&2
            return 1 ;;
    esac

    remote_sha=$2
    if [ -z "$remote_sha" ]; then
        # core.hooksPath is shared, so the pre-push that runs is the main checkout's, which may predate passing
        # the remote sha. Fall back to the remote-tracking ref; never fetched means autodev is being created.
        remote_sha=$(git rev-parse -q --verify "${AUTODEV_TRACKING_REF:-refs/remotes/origin/autodev}" 2>/dev/null) || remote_sha=0
    fi

    case "$remote_sha" in
        *[!0]*) ;;
        *) return 0 ;;
    esac

    git merge-base --is-ancestor "$remote_sha" "$1" 2>/dev/null && return 0
    echo "pre-push: autodev only fast-forwards. git fetch, merge origin/autodev into your commit, and push again." >&2
    echo "          Never force-push it: merged work from other sessions would be lost." >&2
    echo "          Override (only when the user asks): ALLOW_AUTODEV_REWRITE=1 git push ..." >&2
    return 1
}
