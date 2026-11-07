#!/bin/sh
# Tests the branch policy enforced by .githooks (branch-policy.sh + the pre-push hook itself).
# Run after changing any hook: sh tools/test-git-hooks.sh   (exit 0 = all passed)

repo_root=$(cd "$(dirname "$0")/.." && pwd)

unset ALLOW_MASTER_COMMIT ALLOW_MASTER_PUSH ALLOW_AUTODEV_REWRITE ALLOW_DOC_ERRORS AUTODEV_TRACKING_REF
. "$repo_root/.githooks/branch-policy.sh"

sha=1234567890abcdef1234567890abcdef12345678
zero=0000000000000000000000000000000000000000
# Real commits, so autodev's fast-forward check (git merge-base --is-ancestor) has something to walk: a throwaway
# repo with two commits and a copy of the hooks (this repo may have only its root commit). The tests run inside it.
hist=$(mktemp -d)
(
    cd "$hist" || exit 1
    git init -q . && git config user.email test@example.com && git config user.name test && git config core.autocrlf false
    mkdir -p .githooks && cp "$repo_root/.githooks/branch-policy.sh" "$repo_root/.githooks/pre-push" .githooks/
    git add . && git commit -qm one && echo two > two.txt && git add . && git commit -qm two
) >/dev/null 2>&1
cd "$hist" || exit 1
head_sha=$(git rev-parse HEAD)
parent_sha=$(git rev-parse HEAD~1)
failures=0

# expect <pass|fail> <description> <command...>
expect() {
    want=$1; desc=$2; shift 2
    if "$@" 2>/dev/null; then got=pass; else got=fail; fi
    if [ "$got" = "$want" ]; then
        echo "ok   $desc"
    else
        echo "FAIL $desc (expected $want, got $got)"
        failures=$((failures + 1))
    fi
}

commit_with_override() { ALLOW_MASTER_COMMIT=1; check_commit_branch "$1"; r=$?; unset ALLOW_MASTER_COMMIT; return $r; }
push_with_override() { ALLOW_MASTER_PUSH=1; check_push_ref "$1" "$2"; r=$?; unset ALLOW_MASTER_PUSH; return $r; }
autodev_rewrite_with_override() { ALLOW_AUTODEV_REWRITE=1; check_push_ref "$1" "$2" "$3"; r=$?; unset ALLOW_AUTODEV_REWRITE; return $r; }
push_autodev_tracking() { AUTODEV_TRACKING_REF=$1; check_push_ref refs/heads/autodev "$2"; r=$?; unset AUTODEV_TRACKING_REF; return $r; }
run_pre_push() { printf '%s' "$1" | sh "$hist/.githooks/pre-push"; }

# Hook entry points must be committed executable (100755): a 100644 hook is skipped on Linux and macOS with a hint.
hook_committed_executable() { git -C "$repo_root" ls-files -s ".githooks/$1" | grep -q '^100755 '; }
expect pass "pre-commit committed as 100755"         hook_committed_executable pre-commit
expect pass "pre-merge-commit committed as 100755"   hook_committed_executable pre-merge-commit
expect pass "pre-push committed as 100755"           hook_committed_executable pre-push

# Commits
expect pass "commit on feature/<name>"               check_commit_branch feature/chart-filters
expect pass "commit on fix/<name>"                   check_commit_branch fix/tax-rounding
expect pass "commit on detached HEAD (rebase)"       check_commit_branch ""
expect fail "commit on master"                       check_commit_branch master
expect pass "commit on master with override"         commit_with_override master
expect fail "commit on chore/<name>"                 check_commit_branch chore/cleanup
expect fail "commit on claude/<name>"                check_commit_branch claude/some-session
expect fail "commit on bare 'feature/' (no name)"    check_commit_branch feature/
expect fail "commit on 'feature' (no slash)"         check_commit_branch feature
expect fail "commit on 'features/<name>'"            check_commit_branch features/thing
expect pass "commit on autodev"                      check_commit_branch autodev
expect fail "commit on 'autodev-2'"                  check_commit_branch autodev-2
expect fail "commit on 'autodev/<name>'"             check_commit_branch autodev/thing
# <name> is kebab-case: lowercase words and digits joined by single hyphens.
expect pass "commit on kebab-case with digits"       check_commit_branch feature/v2-ghost-recompute
expect fail "commit on feature/Bad_Name"             check_commit_branch feature/Bad_Name
expect fail "commit on an uppercase name"            check_commit_branch fix/Rounding
expect fail "commit on a snake_case name"            check_commit_branch feature/bad_name
expect fail "commit on a doubled hyphen"             check_commit_branch feature/bad--name
expect fail "commit on a leading hyphen"             check_commit_branch fix/-bad
expect fail "commit on a trailing hyphen"            check_commit_branch fix/bad-
expect fail "commit on a nested name"                check_commit_branch feature/area/thing

# Pushes
expect pass "push feature/<name>"                    check_push_ref refs/heads/feature/x "$sha"
expect pass "push fix/<name>"                        check_push_ref refs/heads/fix/x "$sha"
expect pass "push a tag"                             check_push_ref refs/tags/v0.0.21 "$sha"
expect pass "delete a non-conforming remote branch"  check_push_ref refs/heads/New-Main "$zero"
expect fail "push master"                            check_push_ref refs/heads/master "$sha"
expect fail "delete remote master"                   check_push_ref refs/heads/master "$zero"
expect pass "push master with override"              push_with_override refs/heads/master "$sha"
expect fail "push chore/<name>"                      check_push_ref refs/heads/chore/x "$sha"
expect fail "push bare 'fix/' (no name)"             check_push_ref refs/heads/fix/ "$sha"
expect pass "push a kebab-case name"                 check_push_ref refs/heads/fix/tax-drift-2 "$sha"
expect fail "push feature/Bad_Name"                  check_push_ref refs/heads/feature/Bad_Name "$sha"
expect pass "delete a remote feature/Bad_Name"       check_push_ref refs/heads/feature/Bad_Name "$zero"

# autodev: created and fast-forwarded freely, never deleted or rewritten
expect pass "create remote autodev"                  check_push_ref refs/heads/autodev "$head_sha" "$zero"
expect pass "fast-forward autodev"                   check_push_ref refs/heads/autodev "$head_sha" "$parent_sha"
expect pass "push autodev unchanged"                 check_push_ref refs/heads/autodev "$head_sha" "$head_sha"
expect fail "force-push autodev backwards"           check_push_ref refs/heads/autodev "$parent_sha" "$head_sha"
expect fail "push autodev over an unfetched sha"     check_push_ref refs/heads/autodev "$head_sha" "$sha"
# An older pre-push (the main checkout's, via the shared core.hooksPath) passes no remote sha: use the tracking ref.
expect pass "no remote sha, tracking ref behind"     push_autodev_tracking HEAD~1 "$head_sha"
expect fail "no remote sha, tracking ref ahead"      push_autodev_tracking HEAD "$parent_sha"
expect pass "no remote sha, never fetched (create)"  push_autodev_tracking refs/remotes/origin/no-such-branch "$head_sha"
expect fail "delete remote autodev"                  check_push_ref refs/heads/autodev "$zero" "$head_sha"
expect pass "force-push autodev with override"       autodev_rewrite_with_override refs/heads/autodev "$parent_sha" "$head_sha"
expect fail "push 'autodev-2'"                       check_push_ref refs/heads/autodev-2 "$sha" "$zero"

# The real pre-push hook: one bad ref in a multi-ref push fails the whole push.
expect pass "pre-push hook, conforming refs only" \
    run_pre_push "refs/heads/feature/a $sha refs/heads/feature/a $zero
refs/heads/fix/b $sha refs/heads/fix/b $zero
"
expect fail "pre-push hook, one non-conforming ref" \
    run_pre_push "refs/heads/feature/a $sha refs/heads/feature/a $zero
refs/heads/chore/b $sha refs/heads/chore/b $zero
"
# ...and hands each ref's remote sha to the autodev check.
expect pass "pre-push hook, autodev fast-forward" \
    run_pre_push "refs/heads/autodev $head_sha refs/heads/autodev $parent_sha
"
expect fail "pre-push hook, autodev force-push" \
    run_pre_push "refs/heads/autodev $parent_sha refs/heads/autodev $head_sha
"

cd "$repo_root" || exit 1
rm -rf "$hist"

# Doc checks (.githooks/doc-checks.sh, run by pre-commit), against a throwaway repo holding copies of the hooks and
# the checkers: the real pre-commit runs okf.py check, check-doc-links.py and check-skills.py only when knowledge/,
# .claude/skills or CLAUDE.md is staged, and refuses on errors.
. "$repo_root/.githooks/doc-checks.sh"
docs_staged_of() { printf '%s\n' "$@" | docs_staged; }
expect pass "docs_staged: a knowledge/ path"                 docs_staged_of "knowledge/testing.md"
expect pass "docs_staged: a knowledge/ path among others"   docs_staged_of "Mod/Thing.cs" "knowledge/testing.md"
expect pass "docs_staged: a skill"                        docs_staged_of ".claude/skills/autodev/SKILL.md"
expect pass "docs_staged: an agent definition"             docs_staged_of ".claude/agents/test-agent.md"
expect pass "docs_staged: CLAUDE.md"                      docs_staged_of "CLAUDE.md"
expect pass "docs_staged: a tools/ script (the orphan-tool guard)" docs_staged_of "Mod/Thing.cs" "tools/okf.py"
expect pass "docs_staged: a tool skills may name"         docs_staged_of "tools/okf.py"
expect fail "docs_staged: code only"                      docs_staged_of "Mod/Thing.cs" "UI/src/Panel.tsx"
expect fail "docs_staged: a look-alike path"              docs_staged_of "UI/knowledge/x.md" "knowledgex.md" "sub/CLAUDE.md" ".claude/agentsx/a.md" "Mod/tools/x.cs" "sub/tools/x.py" "toolsx/a.py"
expect fail "docs_staged: nothing staged"                 docs_staged_of ""

if doc_py=$(doc_check_python); then
    doc_scratch=$(mktemp -d)
    (
        cd "$doc_scratch" || exit 1
        git init -q . && git config user.email test@example.com && git config user.name test && git config core.autocrlf false
        git checkout -q -b feature/doc-checks
        mkdir -p .githooks tools knowledge
        cp "$repo_root/.githooks/branch-policy.sh" "$repo_root/.githooks/doc-checks.sh" "$repo_root/.githooks/pre-commit" \
            "$repo_root/.githooks/pre-merge-commit" .githooks/
        cp "$repo_root/tools/okf.py" "$repo_root/tools/check-doc-links.py" "$repo_root/tools/check-skills.py" tools/
        printf '%s\n' '---' 'type: Reference' 'title: Guide' 'description: A guide.' '---' '' '# Guide' '' '## Usage' > knowledge/guide.md
        echo "# Project" > CLAUDE.md
        PYTHONDONTWRITEBYTECODE=1 "$doc_py" tools/okf.py index
        git add . && git commit -qm base
    ) >/dev/null 2>&1
    in_doc() { (cd "$doc_scratch" && "$@"); }
    doc_hook() { (cd "$doc_scratch" && sh .githooks/pre-commit); }
    doc_hook_says() { (cd "$doc_scratch" && sh .githooks/pre-commit 2>&1 >/dev/null | grep -q "$1"); }
    doc_hook_override() { (cd "$doc_scratch" && ALLOW_DOC_ERRORS=1 sh .githooks/pre-commit); }
    doc_stage() { (cd "$doc_scratch" && git reset -q && git add -- "$@"); }
    doc_restore() { (cd "$doc_scratch" && git reset -q && git checkout -q -- . && git clean -qfd); }
    doc_write() { dw_path=$1; shift; (cd "$doc_scratch" && mkdir -p "$(dirname "$dw_path")" && printf '%s\n' "$@" > "$dw_path"); }

    doc_write knowledge/guide.md '---' 'type: Reference' 'title: Guide' 'description: A guide.' '---' '' '# Guide' '' '## Usage' 'See [usage](#usage).'
    doc_stage knowledge/guide.md
    expect pass "pre-commit: a valid knowledge/ change commits"          doc_hook

    doc_write knowledge/guide.md '---' 'type: Reference' 'title: Guide' 'description: A guide.' '---' '' '# Guide' '' 'See [gone](gone.md).'
    doc_stage knowledge/guide.md
    expect fail "pre-commit: a staged broken link is refused"         doc_hook
    expect pass "pre-commit: ...and names check-doc-links.py"         doc_hook_says "check-doc-links.py"
    expect pass "pre-commit: ...unless ALLOW_DOC_ERRORS=1"            doc_hook_override
    doc_write CLAUDE.md '# Project' '' 'More.'
    doc_stage CLAUDE.md
    expect fail "pre-commit: staging CLAUDE.md runs the doc checks"   doc_hook
    doc_write Mod/Thing.cs 'class Thing {}'
    doc_stage Mod/Thing.cs
    expect pass "pre-commit: code-only commit skips the doc checks"   doc_hook
    doc_restore

    # A new tools/ file nothing documents is refused (check-doc-links.py's orphan-tool guard), until repo-structure.md names it.
    doc_write tools/orphan-tool.py 'print(1)'
    doc_stage tools/orphan-tool.py
    expect fail "pre-commit: a tools/ file no doc, skill or hook names is refused" doc_hook
    expect pass "pre-commit: ...and names the orphan"                      doc_hook_says "tools/orphan-tool.py"
    doc_write knowledge/repo-structure.md '---' 'type: Reference' 'title: Repo structure' 'description: The folders.' '---' '' '# Repo structure' '' '`orphan-tool.py` does a thing.'
    in_doc env PYTHONDONTWRITEBYTECODE=1 "$doc_py" tools/okf.py index >/dev/null 2>&1
    doc_stage tools/orphan-tool.py knowledge
    expect pass "pre-commit: ...and commits once repo-structure.md names it"        doc_hook
    doc_restore

    doc_write knowledge/new.md '---' 'type: Reference' 'title: New' 'description: Not indexed yet.' '---' '' '# New'
    doc_stage knowledge/new.md
    expect fail "pre-commit: a doc missing from its index is refused" doc_hook
    expect pass "pre-commit: ...and names okf.py check"               doc_hook_says "okf.py check"
    in_doc env PYTHONDONTWRITEBYTECODE=1 "$doc_py" tools/okf.py index >/dev/null
    doc_stage knowledge/new.md knowledge/index.md
    expect pass "pre-commit: ...and passes once indexed"              doc_hook
    doc_restore

    doc_write .claude/skills/bad/SKILL.md '---' 'name: bad' 'description: Build a thing (visual only): a preset. Use when the user says /bad, asks for a thing, a widget, a gadget or anything at all like those.' '---' '# Bad'
    doc_stage .claude/skills/bad/SKILL.md
    expect fail "pre-commit: a skill whose frontmatter won't parse is refused" doc_hook
    expect pass "pre-commit: ...and names check-skills.py"                    doc_hook_says "check-skills.py"
    doc_write .claude/skills/bad/SKILL.md '---' 'name: bad' 'description: "Build a thing (visual only): a preset. Use when the user says /bad, asks for a thing, a widget, a gadget or anything at all like those."' '---' '# Bad'
    doc_stage .claude/skills/bad/SKILL.md
    expect pass "pre-commit: ...and commits once quoted"                      doc_hook
    doc_restore

    # pre-merge-commit: a real `git merge` (the hooks path set for that command only) runs it. A merge whose
    # tree puts a doc over its budget prints a warning naming it and still merges; a merge that fits prints nothing.
    doc_branch_doc() {   # <branch> <file> <bytes>: a branch off feature/doc-checks that commits knowledge/<file>
        in_doc git checkout -q -b "$1" feature/doc-checks
        doc_big "knowledge/$2" Merged "$3"
        in_doc env PYTHONDONTWRITEBYTECODE=1 "$doc_py" tools/okf.py index >/dev/null
        in_doc git add knowledge
        in_doc git commit -q --no-verify -m "$2"
        in_doc git checkout -q feature/doc-checks
    }
    doc_merge() { (cd "$doc_scratch" && git -c core.hooksPath=.githooks merge -q --no-ff --no-edit "$1" 2>"$doc_scratch.merge-err"); }
    doc_merge_quiet() { [ ! -s "$doc_scratch.merge-err" ]; }
    doc_merge_says() { grep -q "$1" "$doc_scratch.merge-err"; }
    doc_merge_reset() { in_doc git reset -q --hard "$1"; }
    doc_big() { in_doc env PYTHONDONTWRITEBYTECODE=1 "$doc_py" -c "import sys; open(sys.argv[1], 'w', newline='\n').write('---\ntype: Reference\ntitle: ' + sys.argv[2] + '\ndescription: Sized.\n---\n\n# Sized\n\n' + 'x' * int(sys.argv[3]) + '\n')" "$@"; }
    doc_before_merge=$(in_doc git rev-parse HEAD)
    doc_branch_doc feature/merge-over merged.md 42000
    doc_branch_doc feature/merge-fits fits.md 100
    expect pass "pre-merge-commit: a merge that puts a doc over its budget still merges"  doc_merge feature/merge-over
    expect pass "pre-merge-commit: ...with a warning naming the doc"                     doc_merge_says "ERROR knowledge/merged.md: \[budget\]"
    expect fail "pre-merge-commit: ...(the warning is printed)"                          doc_merge_quiet
    doc_merge_reset "$doc_before_merge"
    expect pass "pre-merge-commit: a merge that fits merges"                             doc_merge feature/merge-fits
    expect pass "pre-merge-commit: ...and prints nothing"                                doc_merge_quiet
    doc_merge_reset "$doc_before_merge"
    rm -f "$doc_scratch.merge-err"

    # Size budgets are merge-safe: only a staged doc over its budget refuses. An unstaged one (an overflow another
    # branch's merge left on the tree) prints a warning naming it and lets an unrelated docs commit through.
    doc_big knowledge/small.md Small 100
    doc_big knowledge/over.md Over 42000
    in_doc env PYTHONDONTWRITEBYTECODE=1 "$doc_py" tools/okf.py index >/dev/null
    in_doc git add knowledge
    in_doc git commit -q --no-verify -m "a merge left over.md over its budget"
    doc_big knowledge/small.md Small 200
    doc_stage knowledge/small.md
    expect pass "pre-commit: staging doc A while unstaged doc B is over budget commits"  doc_hook
    expect pass "pre-commit: ...with a warning naming B"                        doc_hook_says "knowledge/over.md: \[budget\] not staged"
    doc_big knowledge/over.md Over 42100
    doc_stage knowledge/over.md
    expect fail "pre-commit: staging an over-budget doc is refused"             doc_hook
    expect pass "pre-commit: ...and names it"                                   doc_hook_says "ERROR knowledge/over.md: \[budget\]"
    rm -rf "$doc_scratch"
else
    echo "skip doc-check hook tests: no working python"
fi

if [ "$failures" -eq 0 ]; then
    echo "All hook tests passed."
    exit 0
fi

echo "$failures hook test(s) failed."
exit 1
