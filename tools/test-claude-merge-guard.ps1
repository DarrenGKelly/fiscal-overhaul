<#
.SYNOPSIS
    Tests tools/claude-merge-guard.ps1 (the rules) and tools/claude-merge-guard.sh (the hook entry point).

.DESCRIPTION
    No network: the rules read only the command text, and the end-to-end cases never reach gh.
    Run after changing tools/claude-merge-guard.*:  ./tools/test-claude-merge-guard.ps1   (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'claude-merge-guard.ps1')

$failures = 0
function Check([bool]$Condition, [string]$Description, [string]$Detail = '') {
    if ($Condition) {
        Write-Host "ok   $Description"
    }
    else {
        Write-Host "FAIL $Description" -ForegroundColor Red
        if ($Detail) { Write-Host ($Detail.Trim() -replace '(?m)^', '       ') }
        $script:failures++
    }
}

function Expect([string]$Want, [string]$Description, [string]$Command) {
    $verdict = Get-MergeGuardVerdict -Command $Command
    $got = if ($verdict) { 'block' } else { 'allow' }
    Check ($got -eq $Want) $Description "expected $Want, got $($got): $Command`n$verdict"
}

# ---------------------------------------------------------------------------
# gh pr merge: blocked as a command whatever the base (land with land-pr.ps1); text that only mentions it is not
# ---------------------------------------------------------------------------

$verdict = Get-MergeGuardVerdict -Command 'gh pr merge 41 --squash --delete-branch'
Check ($verdict -match 'land-pr\.ps1 <N>') 'a direct merge is blocked with "land with ./tools/land-pr.ps1 <N>"' $verdict
Expect block 'merge a PR into autodev (land-pr does that)' 'gh pr merge 41 --squash --delete-branch'
Expect block 'merge a PR into master' 'gh pr merge 42 --squash --delete-branch'
Expect block 'merge by URL, full gh path from PowerShell' '& "C:\Program Files\GitHub CLI\gh.exe" pr merge https://github.com/example-owner/fiscal-overhaul/pull/41 --squash'
Expect block 'merge, full gh path from Bash' '"/c/Program Files/GitHub CLI/gh.exe" pr merge 42 --squash'
Expect block 'merge, gh.exe by name' 'gh.exe pr merge 41 --squash'
Expect block 'merge with no PR number (the current branch''s PR)' 'gh pr merge --squash'
Expect block 'merge by branch name' 'gh pr merge feature/x --squash --delete-branch'
Expect block 'merge with extra spaces' 'gh  pr   merge 41'
Expect block 'merge after &&' 'git status && gh pr merge 41 --squash'
Expect block 'merge after ;' 'cd repo; gh pr merge 41'
Expect block 'merge after ||' 'false || gh pr merge 41'
Expect block 'merge after a pipe' 'echo y | gh pr merge 41'
Expect block 'merge on a later line' "git status`ngh pr merge 41 --squash"
Expect block 'merge on a later CRLF line' "git status`r`ngh pr merge 41 --squash"
Expect block 'merge after an env assignment' 'GH_REPO=o/r gh pr merge 41'
Expect block 'merge through env' 'env GH_REPO=o/r gh pr merge 41'
Expect block 'merge through a variable (Bash)' '$GH pr merge 41 --squash'
Expect block 'merge through a quoted variable (Bash)' '"$GH" pr merge 41 --squash'
Expect block 'merge through a variable (PowerShell call operator)' '& $gh pr merge 41 --squash'
Expect block 'merge in a subshell' '(cd repo && gh pr merge 41)'
Expect block 'merge in a command substitution' 'x=$(gh pr merge 41)'
Expect block 'merge in a command substitution inside double quotes' 'echo "$(gh pr merge 41)"'
Expect block 'merge in a PowerShell script block' '& { gh pr merge 41 }'
Expect block 'merge through bash -c' 'bash -c "gh pr merge 41 --squash"'
Expect block 'merge through pwsh -Command' 'pwsh -NoProfile -Command ''gh pr merge 41'''
Expect block 'merge after a heredoc ends' "cat > f.md <<'EOF'`nnotes`nEOF`ngh pr merge 41"
Expect allow 'land-pr itself (its own merge is a child process the hook never sees)' './tools/land-pr.ps1 41'
Expect allow 'land-pr by branch, from PowerShell' '& ./tools/land-pr.ps1 feature/x'
Expect allow 'land-pr through pwsh -File' 'pwsh -File tools/land-pr.ps1 41'
Expect allow 'a grep for the merge command (double quotes)' 'grep -rn "gh pr merge" knowledge'
Expect allow 'a grep for the merge command (single quotes, alternation)' 'grep -rn -E ''land-pr|gh pr merge'' knowledge tools'
Expect allow 'Select-String for the merge command' 'Select-String -Path knowledge/*.md -Pattern ''gh pr merge'''
Expect allow 'git log --grep for the merge command' 'git log --oneline --grep="gh pr merge"'
Expect allow 'a commit message that mentions it' 'git commit -m "guard: block gh pr merge 41 --squash"'
Expect allow 'a commit message with an escaped quote, then the command name' 'git commit -m "a \"quote\" then gh pr merge 41"'
Expect allow 'a commit message on its own line inside quotes' "git commit -m `"title`n`ngh pr merge 41 is blocked now`""
Expect allow 'a heredoc body that starts a line with it' "git commit -F - <<'EOF'`ngh pr merge 41 --squash`nEOF"
Expect allow 'an unquoted heredoc delimiter, <<-' "cat <<-EOF > notes.md`n`tgh pr merge 41`n`tEOF`ngit status"
Expect allow 'a PowerShell here-string that starts a line with it' "`$body = @'`ngh pr merge 41 --squash`n'@`nSet-Content b.md `$body"
Expect allow 'a double-quoted PowerShell here-string' "`$body = @`"`ngh pr merge 41`n`"@"
Expect allow 'echo the command (an argument, not a command)' 'echo gh pr merge 41'
Expect allow 'view a PR''s mergeability' 'gh pr view 41 --json mergeable,mergeStateStatus'
Expect allow 'create and view PRs into master' 'gh pr create --base master --title "v0.0.28 - x" --body-file b.md && gh pr view 42'
Expect allow 'a git merge (not gh)' 'git fetch && git merge --no-edit origin/autodev'

# ---------------------------------------------------------------------------
# gh pr create / gh issue create need --body-file
# ---------------------------------------------------------------------------

Expect block 'gh pr create without --body-file' 'gh pr create --base autodev --title x'
Expect allow 'gh pr create with --body-file' 'gh pr create --base autodev --title x --body-file f.md'
Expect allow 'gh pr create with -F' 'gh pr create --base autodev --title x -F f.md'
Expect allow 'gh pr create with --body-file=' 'gh pr create --base autodev --title x --body-file=f.md'
Expect block 'gh pr create with an inline --body' 'gh pr create --base autodev --title x --body "text"'
Expect block 'gh issue create without --body-file' 'gh issue create --title x --label bug'
Expect allow 'gh issue create with --body-file' 'gh issue create --title x --label bug --body-file f.md'
Expect allow 'gh issue create, full gh path from PowerShell, --body-file' '& "C:\Program Files\GitHub CLI\gh.exe" issue create --title "a & b; c" --body-file f.md'
Expect block 'gh pr create, full gh path from Bash, no body file' '"/c/Program Files/GitHub CLI/gh.exe" pr create --base autodev --title x'
Expect block '--body-file only inside the quoted title doesn''t count' 'gh pr create --title "use --body-file x" --base autodev'
Expect block '--body-file on a later command doesn''t count' 'gh pr create --title x && echo --body-file f'
Expect block 'gh issue comment without --body-file' 'gh issue comment 42 --body "x"'
Expect allow 'gh issue comment with --body-file' 'gh issue comment 42 --body-file c.md'
Expect block 'gh pr comment without --body-file' 'gh pr comment 42'
Expect allow 'gh pr view and gh issue list need no body' 'gh pr view 42 && gh issue list --label bug'
Expect allow 'a grep for gh pr create (a pattern, not a command)' 'grep -rn "gh pr create" knowledge'
Expect allow 'a heredoc that mentions gh issue comment' "cat > notes.md <<'EOF'`ngh issue comment 42 --body x`nEOF"
Expect allow 'a commit message that mentions gh pr create' 'git commit -m "docs: gh pr create needs a body file"'
Expect block 'gh issue comment with an inline --body, full gh path' '& "C:\Program Files\GitHub CLI\gh.exe" issue comment 42 --body "done"'
Expect block 'gh pr create without --body-file inside bash -c' 'bash -c "gh pr create --base autodev --title x"'

# ---------------------------------------------------------------------------
# gh api
# ---------------------------------------------------------------------------

Expect block 'merge a PR through the REST API' 'gh api -X PUT repos/o/r/pulls/42/merge -f merge_method=squash'
Expect block 'merge branches through the REST API' 'gh api repos/o/r/merges -f base=master -f head=autodev'
Expect block 'merge through GraphQL' 'gh api graphql -f query="mutation { mergePullRequest(input: {pullRequestId: 1}) { clientMutationId } }"'
Expect allow 'read a PR through the REST API' 'gh api --paginate repos/o/r/pulls/42/files'
Expect block 'merge through GraphQL, full gh path (the mutation is quoted)' '& "C:\Program Files\GitHub CLI\gh.exe" api graphql -f query="mutation { mergePullRequest(input: {}) { clientMutationId } }"'
Expect allow 'a grep for a gh api merge call' 'grep -rn "gh api -X PUT repos/o/r/pulls/42/merge" knowledge'
Expect allow 'a grep for gh repo delete' 'grep -rn "gh repo delete" knowledge'
Expect block 'gh api DELETE inside bash -c' 'bash -c "gh api -X DELETE repos/o/r/git/refs/heads/master"'

# ---------------------------------------------------------------------------
# Branches and the repository on GitHub
# ---------------------------------------------------------------------------

Expect block 'delete a branch through the REST API' 'gh api -X DELETE repos/o/r/git/refs/heads/master'
Expect block 'force-update a ref through the REST API (-X after the path)' 'gh api repos/o/r/git/refs/heads/master -X PATCH -F sha=abc -F force=true'
Expect block 'create a ref with fields (implicit POST)' 'gh api repos/{owner}/{repo}/git/refs -f ref=refs/heads/master -f sha=abc'
Expect block 'rename a branch through the REST API' '"/c/Program Files/GitHub CLI/gh.exe" api repos/o/r/branches/master/rename -f new_name=old'
Expect block 'delete the repository through the REST API' 'gh api --method DELETE repos/o/r'
Expect block 'change the default branch through the REST API' 'gh api -XPATCH repos/o/r -f default_branch=autodev'
Expect block 'delete a ref through GraphQL' 'gh api graphql -f query="mutation { deleteRef(input: {refId: 1}) { clientMutationId } }"'
Expect block 'gh repo delete' 'gh repo delete example-owner/fiscal-overhaul --yes'
Expect block 'gh repo edit, full gh path from PowerShell' '& "C:\Program Files\GitHub CLI\gh.exe" repo edit --default-branch autodev'
Expect block 'gh repo rename' 'gh repo rename fiscal-overhaul-old'
Expect allow 'read a ref through the REST API' 'gh api repos/o/r/git/refs/heads/master'
Expect allow 'read the repository through the REST API' 'gh api repos/o/r --jq .default_branch'
Expect allow 'read a branch through the REST API' 'gh api repos/o/r/branches/autodev'
Expect allow 'create an issue (POST, not a branch or the repo)' 'gh api repos/o/r/issues -f title=x'
Expect allow 'read through GraphQL' 'gh api graphql -f query="{ viewer { login } }"'
Expect allow 'gh repo view' 'gh repo view --json name,defaultBranchRef'

# ---------------------------------------------------------------------------
# Skipping git hooks
# ---------------------------------------------------------------------------

Expect block 'git commit --no-verify' 'git commit --no-verify -m "x"'
Expect block 'git push --no-verify' 'git push --no-verify origin autodev'
Expect block 'git commit -n' 'git commit -n -m "x"'
Expect block 'git commit -nm' 'git commit -nm "x"'
Expect allow 'plain commit' 'git commit -m "x"'
Expect allow 'git commit -am, --amend --no-edit' 'git commit -am "x" && git commit --amend --no-edit'
Expect allow '-n inside the commit message' 'git commit -m "support the -n flag"'
Expect allow 'git push -n (dry run)' 'git push -n origin feature/x'
Expect allow '--no-verify inside the commit message' 'git commit -m "never use --no-verify"'
Expect allow '--no-verify in a heredoc commit message' "git commit -F - <<'EOF'`ngit push --no-verify is blocked`nEOF"
Expect allow 'a grep for git commit --no-verify' 'grep -rn "git commit --no-verify" knowledge'
Expect block 'git commit -n after the message' 'git commit -m "x" -n'
Expect block '--no-verify after a quoted message' 'git commit -m "x" --no-verify'
Expect block 'git -c core.hooksPath=<elsewhere>' 'git -c core.hooksPath=/dev/null commit -m "x"'
Expect block 'git config core.hooksPath <elsewhere>' 'git config core.hooksPath "C:/no-hooks"'
Expect block 'git config --global core.hooksPath <elsewhere>' 'git config --global core.hooksPath /dev/null'
Expect block 'git -c "core.hooksPath=<elsewhere>" (quoted)' 'git -c "core.hooksPath=C:/no-hooks" push origin autodev'
Expect block 'git config --unset core.hooksPath' 'git config --unset core.hooksPath'
Expect allow 'enable the repo hooks' 'git config core.hooksPath .githooks'
Expect allow 'enable the repo hooks by absolute path' 'git config core.hooksPath "C:\Dev\fiscal-overhaul\.githooks"'
Expect allow 'read core.hooksPath' 'git config core.hooksPath && git status'
Expect allow 'core.hooksPath mentioned in a commit message' 'git commit -m "x" -m "core.hooksPath is shared, so x"'
Expect allow 'merge master into autodev with git' 'git fetch && git merge origin/master'
Expect allow 'unrelated command' 'git status --short && ls'

# ---------------------------------------------------------------------------
# Git-hook overrides (ALLOW_*): allowed, with a warning
# ---------------------------------------------------------------------------

function ExpectWarn([bool]$Want, [string]$Description, [string]$Command) {
    $verdict = Get-MergeGuardVerdict -Command $Command
    $warning = Get-MergeGuardWarning -Command $Command
    Check (-not $verdict -and [bool]$warning -eq $Want) $Description "expected allow with warning=$Want, got verdict '$verdict', warning '$warning': $Command"
}

ExpectWarn $true 'ALLOW_DOC_ERRORS=1 before git commit warns (still allowed)' 'ALLOW_DOC_ERRORS=1 git commit -m x'
ExpectWarn $true 'ALLOW_MASTER_PUSH=1 before git push warns' 'ALLOW_MASTER_PUSH=1 git push origin master'
ExpectWarn $true 'export ALLOW_MASTER_COMMIT=1 warns' 'export ALLOW_MASTER_COMMIT=1; git commit -m x'
ExpectWarn $true 'env ALLOW_AUTODEV_REWRITE=1 warns' 'env ALLOW_AUTODEV_REWRITE=1 git push origin autodev'
ExpectWarn $true 'PowerShell $env:ALLOW_DOC_ERRORS = ''1'' warns' '$env:ALLOW_DOC_ERRORS = ''1''; git commit -m x'
ExpectWarn $true 'override chained after cd warns' 'cd repo && ALLOW_MASTER_PUSH=1 git push origin master'
ExpectWarn $false 'a commit message that mentions ALLOW_DOC_ERRORS doesn''t warn' 'git commit -m "ALLOW_DOC_ERRORS docs"'
ExpectWarn $false 'a quoted VAR=1 in a commit message doesn''t warn' 'git commit -m "explain ALLOW_MASTER_PUSH=1 in the docs"'
ExpectWarn $false 'a plain commit doesn''t warn' 'git commit -m x'
ExpectWarn $false 'reading an override doesn''t warn' 'echo $ALLOW_DOC_ERRORS'
ExpectWarn $false 'a longer variable name doesn''t warn' 'MY_ALLOW_DOC_ERRORS_X=1 git commit -m x'
ExpectWarn $true 'land-pr -SkipBudget warns (still allowed)' './tools/land-pr.ps1 41 -SkipBudget'
ExpectWarn $false 'plain land-pr doesn''t warn' './tools/land-pr.ps1 41'
ExpectWarn $false 'a commit message that mentions -SkipBudget doesn''t warn' 'git commit -m "land-pr gets -SkipBudget"'

# ---------------------------------------------------------------------------
# End to end through the hook entry point (sh wrapper -> PowerShell), as Claude Code calls it
# ---------------------------------------------------------------------------

$sh = (Get-Command sh -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $sh) { $sh = 'C:\Program Files\Git\bin\sh.exe' }
$wrapper = Join-Path $PSScriptRoot 'claude-merge-guard.sh'

function Invoke-Hook([string]$Payload) {
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = 0
    $output = $Payload | & $sh $wrapper 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}
function New-Payload([string]$Command) {
    @{ tool_name = 'Bash'; tool_input = @{ command = $Command }; cwd = (Get-Location).Path } | ConvertTo-Json -Compress
}

$r = Invoke-Hook (New-Payload 'git commit --no-verify -m "x"')
Check ($r.ExitCode -eq 2 -and $r.Output -match 'no-verify') 'hook blocks git commit --no-verify with exit 2 and a reason' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'gh pr merge 41 --squash')
Check ($r.ExitCode -eq 2 -and $r.Output -match 'land-pr\.ps1') 'hook blocks gh pr merge (a PR into autodev too) and names land-pr' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'grep -rn "gh pr merge" knowledge')
Check ($r.ExitCode -eq 0 -and -not $r.Output.Trim()) 'hook allows a grep for the merge command, silently' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'grep -rn "gh pr create" knowledge')
Check ($r.ExitCode -eq 0 -and -not $r.Output.Trim()) 'hook allows a grep for gh pr create, silently' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload './tools/land-pr.ps1 41')
Check ($r.ExitCode -eq 0) 'hook allows land-pr' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload './tools/land-pr.ps1 41 -SkipBudget')
Check ($r.ExitCode -eq 0 -and $r.Output -match 'SkipBudget') 'hook allows land-pr -SkipBudget with a warning (passes the pre-filter)' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'gh repo delete example-owner/fiscal-overhaul --yes')
Check ($r.ExitCode -eq 2 -and $r.Output -match 'repo delete') 'hook blocks gh repo delete (passes the pre-filter)' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'gh pr create --base autodev --title x')
Check ($r.ExitCode -eq 2 -and $r.Output -match 'body-file') 'hook blocks gh pr create without --body-file (passes the pre-filter)' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'gh pr create --base autodev --title x --body-file f.md')
Check ($r.ExitCode -eq 0) 'hook allows gh pr create with --body-file' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'gh repo view --json name')
Check ($r.ExitCode -eq 0) 'hook allows gh repo view' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'git commit -m "x"')
Check ($r.ExitCode -eq 0) 'hook allows a plain commit (passes the pre-filter, PowerShell allows it)' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'git status')
Check ($r.ExitCode -eq 0) 'hook allows an unrelated command (pre-filter)' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'ALLOW_DOC_ERRORS=1 git commit -m x')
$json = $null
try { $json = ($r.Output.Trim() | ConvertFrom-Json) } catch { }
Check ($r.ExitCode -eq 0 -and $json -and $json.hookSpecificOutput.hookEventName -eq 'PreToolUse' -and
    $json.hookSpecificOutput.additionalContext -match 'ALLOW_DOC_ERRORS' -and $json.systemMessage -match 'ALLOW_DOC_ERRORS') `
    'hook allows an ALLOW_* override (exit 0) and warns the session through additionalContext JSON' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook (New-Payload 'git commit -m "ALLOW_DOC_ERRORS docs"')
Check ($r.ExitCode -eq 0 -and -not $r.Output.Trim()) 'hook allows a commit message that mentions an override, silently' "exit $($r.ExitCode): $($r.Output)"

$r = Invoke-Hook 'not json, but mentions merge'
Check ($r.ExitCode -eq 0) 'hook allows a malformed payload instead of blocking everything' "exit $($r.ExitCode): $($r.Output)"

if ($failures -eq 0) {
    Write-Host 'All merge guard tests passed.'
    exit 0
}
Write-Host "$failures merge guard test(s) failed."
exit 1
