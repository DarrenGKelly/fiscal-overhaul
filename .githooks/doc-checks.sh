# Doc checks for pre-commit, and warn-only for pre-merge-commit (sourced, not a hook). Tested by tools/test-git-hooks.sh.
# When a commit stages anything under knowledge/, .claude/skills/, .claude/agents/, tools/ or CLAUDE.md, run the doc checks and refuse the commit
# on errors: tools/okf.py check (frontmatter, indexes, size budgets, folder map), tools/check-doc-links.py (links, plus its guards:
# every tools/ file is documented, every folder repo-structure.md maps exists) and tools/check-skills.py (tools/ is covered because
# skills name its scripts and flags, and the orphan-tool guard reads it). Commits that touch none of those paths skip them (the fast path). The checks read the working
# tree, not the index, so an unstaged broken doc also refuses a docs commit: fix it or stash it. The one exception is the
# size budget (okf.py check --staged): only a staged doc over its budget refuses; an unstaged one (an overflow a sibling
# branch's merge left) prints a warning naming it, and so does a staged doc within 2% of its cap (one that grew into
# its last 0.5% refuses). Other warnings never block and aren't printed
# here; okf's [budget] warnings always are.

# docs_staged: reads paths on stdin. Succeeds when any is one the doc checks cover.
docs_staged() {
    while IFS= read -r path; do
        case "$path" in
            knowledge/*|.claude/skills/*|.claude/agents/*|tools/*|CLAUDE.md) return 0 ;;
        esac
    done
    return 1
}

# doc_check_python: prints a python that runs (python3 first: Linux often has no bare python; on Windows python3 can
# be the Store stub, which exists but fails).
doc_check_python() {
    for candidate in python3 python; do
        if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c "" >/dev/null 2>&1; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

# run_doc_check <root> <python> <script> [args...]. A script this checkout doesn't have yet is skipped. Stdin is passed
# on (okf.py check --staged - reads the staged paths from it). Its [budget] warnings print whether it passes or not.
run_doc_check() {
    rdc_root=$1; rdc_py=$2; rdc_script=$3; shift 3
    [ -f "$rdc_root/tools/$rdc_script" ] || return 0
    rdc_status=0
    rdc_out=$(cd "$rdc_root" && PYTHONIOENCODING=utf-8 PYTHONDONTWRITEBYTECODE=1 "$rdc_py" "tools/$rdc_script" "$@" 2>&1) \
        || rdc_status=1
    rdc_budget=$(printf '%s\n' "$rdc_out" | grep '^WARN .*\[budget\]')
    if [ -n "$rdc_budget" ]; then
        echo "pre-commit: size-budget warnings (they don't block this commit):" >&2
        printf '%s\n' "$rdc_budget" | sed 's/^/    /' >&2
    fi
    [ "$rdc_status" = "0" ] && return 0
    echo "pre-commit: python tools/$rdc_script $* found problems:" >&2
    printf '%s\n' "$rdc_out" | grep -v '^WARN ' | sed 's/^/    /' >&2
    return 1
}

# check_staged_docs <repo_root>. Succeeds when no covered path is staged, or when every check passes.
check_staged_docs() {
    staged_paths | docs_staged || return 0
    [ "$ALLOW_DOC_ERRORS" = "1" ] && return 0
    csd_py=$(doc_check_python) || {
        echo "pre-commit: no working python; skipping the doc checks (run them by hand: see knowledge/git-workflow.md#hooks)." >&2
        return 0
    }
    csd_status=0
    # Budget errors count only for staged docs (an okf.py that predates --staged runs the whole-tree check).
    if grep -q -e '--staged' "$1/tools/okf.py" 2>/dev/null; then
        staged_paths | run_doc_check "$1" "$csd_py" okf.py check --staged - || csd_status=1
    else
        run_doc_check "$1" "$csd_py" okf.py check || csd_status=1
    fi
    run_doc_check "$1" "$csd_py" check-doc-links.py || csd_status=1
    run_doc_check "$1" "$csd_py" check-skills.py || csd_status=1
    if [ "$csd_status" != "0" ]; then
        echo "pre-commit: the commit stages knowledge/, .claude/skills, .claude/agents, tools or CLAUDE.md, and the doc checks failed. Fix the" >&2
        echo "            errors above (an out-of-date index: python tools/okf.py index) and commit again." >&2
        echo "            Override (only when the user asks): ALLOW_DOC_ERRORS=1 git commit ..." >&2
    fi
    return $csd_status
}

# warn_merge_budgets <repo_root>: pre-merge-commit's check, warn-only. A clean `git merge` runs no pre-commit, so a merge
# that tips a doc over its size budget (two branches that each fit) would reach the branch silently, and land-pr would
# only refuse the PR later. This runs okf.py budget-tree on the merge's tree (the index, via git write-tree)
# and prints its errors as a warning. It never blocks the merge, and prints nothing when the tree fits, when there's
# no python, or when this checkout's okf.py predates budget-tree.
warn_merge_budgets() {
    grep -q 'budget-tree' "$1/tools/okf.py" 2>/dev/null || return 0
    wmb_py=$(doc_check_python) || return 0
    wmb_tree=$(git -C "$1" write-tree 2>/dev/null) || return 0
    wmb_out=$(cd "$1" && PYTHONIOENCODING=utf-8 PYTHONDONTWRITEBYTECODE=1 "$wmb_py" tools/okf.py budget-tree "$wmb_tree" 2>&1)
    wmb_errors=$(printf '%s\n' "$wmb_out" | grep '^ERROR ')
    [ -n "$wmb_errors" ] || return 0
    echo "pre-merge-commit: this merge puts docs over their size budget (a warning; the merge goes ahead):" >&2
    printf '%s\n' "$wmb_errors" | sed 's/^/    /' >&2
    echo "    Trim or split them before you push: tools/land-pr.ps1 refuses a PR whose merge with autodev is over." >&2
    return 0
}
