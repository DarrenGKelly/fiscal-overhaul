<#
.SYNOPSIS
    Lands a PR into autodev: checks it, squash-merges it at the head sha you pushed, and cleans up its branch.

.DESCRIPTION
    The one command an autonomous session or an orchestrator uses to merge. In order:

      1. Looks the PR up (number or branch name). Refuses unless it is open and its base is autodev:
         Claude merges nothing else, and only through this script: tools/claude-merge-guard blocks a direct
         gh pr merge (this script's own runs as a child process, which the hook never sees).
      2. Refuses when the work isn't all on the PR: a local copy of the branch at another commit (push first),
         or uncommitted tracked changes in the worktree that has it checked out (commit and push first).
         Refuses a PR GitHub calls CONFLICTING (merge origin/autodev into the branch, re-test, push).
         Refuses a PR whose merge with origin/autodev (git merge-tree --write-tree) puts a doc over its size
         budget, checked by that tree's own `tools/okf.py budget-tree`: two branches can each fit a cap and
         overflow it together (merge origin/autodev and trim, or split the doc).
         Refuses a PR whose tool tests fail: for each tools/ or .githooks/ file it changes, tools/test-<stem>.py
         and .sh (a changed test itself; test-git-hooks.sh for .githooks/), plus the PowerShell suites opted in by
         $LandingPowerShellSuites, run in the branch's clean worktree or a temporary one
         (knowledge/testing.md#tool-tests).
      3. Squash-merges with --match-head-commit, so a commit pushed after the check can't slip in untested.
         It doesn't pass --delete-branch: gh's local half of that fails on a branch a worktree has checked
         out.
      4. Deletes the remote branch if GitHub didn't, then the local branch: when this checkout has it checked
         out it detaches first (at the same commit, so nothing on disk changes); when another worktree has it,
         it leaves both, and tools/prune-worktrees.ps1 removes them once that worktree is idle.

    Re-running on a PR that already merged skips to step 4, so an interrupted run can be finished.

.EXAMPLE
    ./tools/land-pr.ps1 712
    ./tools/land-pr.ps1 feature/economy-panel

.NOTES
    Exit 0 = landed (or already landed) and cleaned up; 1 = the merge or a cleanup step failed;
    2 = refused (not into autodev, not open, unpushed or uncommitted work, conflicts, a merged-tree doc budget overflow,
    a failing tool test).
    2 also covers a machine with no working python (the merged-tree budget check can't run); -SkipBudget lands
    without that check, only when the user asks (the merge guard warns on it).
    -GhPath points at another gh (the tests pass a stub). Tests: ./tools/test-land-pr.ps1 (no network).
#>
param(
    [Parameter(Mandatory, Position = 0)] [string]$Pr,
    [string]$GhPath = '',
    [switch]$SkipBudget
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

$AutodevBranch = 'autodev'

function Write-Land([string]$Message, [string]$Colour = 'Gray') { Write-Host "land-pr: $Message" -ForegroundColor $Colour }
function Stop-Land([string]$Message, [int]$Code = 2) { Write-Land $Message 'Red'; exit $Code }
function Invoke-RepoGit([string[]]$Arguments, [string]$At = $repo) { Invoke-Git -Path $At -Arguments $Arguments -AllowFailure }

if (-not $GhPath) {
    $GhPath = Find-Application 'gh' @('C:\Program Files\GitHub CLI\gh.exe')
    if (-not $GhPath) { Stop-Land 'gh not found.' 1 }
}

function Invoke-Gh([string[]]$Arguments) {
    $global:LASTEXITCODE = 0
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $text = & $GhPath @Arguments 2>&1 | Out-String }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text.Trim() }
}

function Get-PullRequest([string]$Selector) {
    $view = Invoke-Gh @('pr', 'view', $Selector, '--json', 'number,state,baseRefName,headRefName,headRefOid,mergeable,mergeCommit')
    if ($view.ExitCode -ne 0) { Stop-Land "Couldn't look up PR '$Selector' (gh pr view): $($view.Text)" }
    try { return $view.Text | ConvertFrom-Json }
    catch { Stop-Land "gh pr view gave something that isn't JSON: $($view.Text)" 1 }
}

$top = Invoke-Git -Path (Get-Location).Path -Arguments @('rev-parse', '--show-toplevel') -AllowFailure
if ($top.ExitCode -ne 0) { Stop-Land 'Run it from a checkout of the repo.' }
$repo = ConvertTo-NormalizedPath $top.Text

function Get-CheckedOutWorktree([string]$Branch) {
    $path = $null
    foreach ($line in ((Invoke-RepoGit @('worktree', 'list', '--porcelain')).Text -split "`r?`n")) {
        if ($line -match '^worktree (.+)$') { $path = ConvertTo-NormalizedPath $Matches[1] }
        elseif ($line -eq "branch refs/heads/$Branch") { return $path }
    }
    return $null
}

function Get-LocalSha([string]$Branch) {
    $result = Invoke-RepoGit @('rev-parse', '--verify', '--quiet', "refs/heads/$Branch")
    if ($result.ExitCode -ne 0) { return $null }
    return $result.Text
}

function Find-Python {
    foreach ($name in 'python3', 'python') {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $command) { continue }
        $global:LASTEXITCODE = 0
        try { & $command.Source -c 'pass' 2>$null | Out-Null } catch { continue }
        if ($LASTEXITCODE -eq 0) { return $command.Source }   # on Windows, python3 can be the Store stub, which fails
    }
    return $null
}

function Save-GitBlob([string]$Object, [string]$Destination) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'git'
    $startInfo.Arguments = Join-ProcessArguments @('-C', $repo, 'cat-file', 'blob', $Object)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $file = [IO.File]::Create($Destination)
    try { $process.StandardOutput.BaseStream.CopyTo($file) } finally { $file.Dispose() }
    $process.WaitForExit()
    [void]$stderrTask.Result
    return $process.ExitCode -eq 0
}

# The doc size budgets on the merge of the PR head with origin/autodev, run with that tree's own tools/okf.py
# (budget-tree). Two branches that each fit a doc's cap can overflow it once both land, and the overflow would then
# block every later docs commit until a fix-up PR trims it; so the PR that would cause it trims or splits instead.
# A tree whose okf.py predates budget-tree skips the check. A machine with no python refuses (exit 2): a skipped check
# is how an overflow reaches autodev. -SkipBudget lands without it, only when the user asks.
function Test-MergedBudget {
    [void](Invoke-RepoGit @('fetch', '--quiet', 'origin', $AutodevBranch, $head))
    if ((Invoke-RepoGit @('cat-file', '-e', "$headSha^{commit}")).ExitCode -ne 0) {
        Stop-Land "Couldn't fetch PR #$number's head ($($headSha.Substring(0, 10))) to check the merged tree's doc budgets." 1
    }
    $merge = Invoke-RepoGit @('merge-tree', '--write-tree', "origin/$AutodevBranch", $headSha)
    if ($merge.ExitCode -eq 1) {
        Stop-Land "PR #$number conflicts with $AutodevBranch (git merge-tree). Merge origin/$AutodevBranch into $head, re-test what the merge touched, push, then run this again."
    }
    if ($merge.ExitCode -ne 0) { Stop-Land "git merge-tree --write-tree failed (git 2.38 or later is needed): $($merge.Error)" 1 }
    $tree = ($merge.Text -split "`r?`n")[0].Trim()
    if ((Invoke-RepoGit @('cat-file', '-e', "${tree}:tools/okf.py")).ExitCode -ne 0) { return }
    $python = Find-Python
    if (-not $python) {
        if ($SkipBudget) { Write-Land 'No working python: -SkipBudget skips the merged-tree doc budget check (python tools/okf.py budget-tree).' 'Yellow'; return }
        Stop-Land 'No working python, so the merged-tree doc budget check (python tools/okf.py budget-tree) can''t run. Install python (python3 or python on PATH) and run this again; -SkipBudget lands without the check, only when the user asks.'
    }
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('fo-land-okf-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $scratch | Out-Null
    try {
        $okf = Join-Path $scratch 'okf.py'
        if (-not (Save-GitBlob "${tree}:tools/okf.py" $okf)) { Stop-Land "Couldn't read tools/okf.py from the merged tree $tree." 1 }
        if (-not (Select-String -LiteralPath $okf -Pattern 'budget-tree' -SimpleMatch -Quiet)) { return }
        $global:LASTEXITCODE = 0
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $output = & $python -I $okf --repo $repo budget-tree $tree 2>&1 | Out-String }
        finally { $ErrorActionPreference = $previous }
        $code = $LASTEXITCODE
    }
    finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
    if ($code -eq 1) {
        $errors = (($output -split "`r?`n") | Where-Object { $_ -like 'ERROR *' }) -join "`n  "
        Stop-Land ("PR #$number fits its doc budgets alone, but merged with $AutodevBranch it is over:`n  $errors`nMerge origin/$AutodevBranch into $head and trim, or split the doc; push, then run this again.")
    }
    if ($code -ne 0) { Stop-Land "The merged-tree doc budget check failed (okf.py budget-tree, exit $code): $output" 1 }
    Write-Land "Doc budgets fit on the merge with $AutodevBranch."
}

# The tool tests of what the PR changes (knowledge/testing.md#tool-tests): for each tools/ or .githooks/
# file the PR changes (against its merge base with origin/autodev), tools/test-<stem>.py and .sh, a changed test
# itself, and tools/test-git-hooks.sh for .githooks/. PowerShell suites run only when listed in
# $LandingPowerShellSuites, opted in after timing (the ones that build throwaway repos take 25-60 s each).
$LandingPowerShellSuites = @('test-claude-merge-guard.ps1', 'test-remove-worktree.ps1')

function Get-ToolTests([string[]]$Changed, [string]$At) {
    $tests = New-Object System.Collections.Generic.List[string]
    foreach ($path in $Changed) {
        $names = @()
        if ($path -like '.githooks/*') { $names = @('test-git-hooks.sh') }
        elseif ($path -like 'tools/*') {
            $leaf = Split-Path -Leaf $path
            $stem = [IO.Path]::GetFileNameWithoutExtension($leaf) -replace '_', '-'
            if ($stem -like 'test-*') {
                if ([IO.Path]::GetExtension($leaf) -in '.py', '.sh' -or $leaf -in $LandingPowerShellSuites) { $names = @($leaf) }
            }
            else { $names = @("test-$stem.py", "test-$stem.sh", "test-$stem.ps1" | Where-Object { $_ -notlike '*.ps1' -or $_ -in $LandingPowerShellSuites }) }
        }
        foreach ($name in $names) {
            if (-not $tests.Contains($name) -and (Test-Path -LiteralPath (Join-Path $At "tools/$name"))) { $tests.Add($name) }
        }
    }
    return , $tests.ToArray()
}

function Find-Sh { return Find-Application 'sh' @('C:\Program Files\Git\bin\sh.exe') }

# Runs the tool tests in a checkout of the PR head: the worktree that has the branch checked out (clean and at the
# head, checked above), else a temporary detached worktree made with hooks off and removed afterwards.
function Test-ToolTests {
    $changed = @(((Invoke-RepoGit @('diff', '--name-only', "origin/$AutodevBranch...$headSha")).Text -split "`r?`n") | Where-Object { $_ })
    if (-not @($changed | Where-Object { $_ -like 'tools/*' -or $_ -like '.githooks/*' }).Count) { return }
    $at = Get-CheckedOutWorktree $head
    $temporary = $null
    if (-not $at) {
        $temporary = Join-Path ([IO.Path]::GetTempPath()) ('fo-land-tools-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        Write-Land "No checkout has $head, so the tool tests run in a temporary worktree at $($headSha.Substring(0, 10))..."
        $add = Invoke-RepoGit @('-c', 'core.hooksPath=.land-pr-no-hooks', 'worktree', 'add', '--quiet', '--detach', $temporary, $headSha)
        if ($add.ExitCode -ne 0) { Stop-Land "Couldn't make a worktree at $($headSha.Substring(0, 10)) for the tool tests: $($add.Error)" 1 }
        $at = $temporary
    }
    $refusal = $null
    try {
        $tests = Get-ToolTests $changed $at
        if (-not $tests.Count) { return }
        $failed = @()
        foreach ($name in $tests) {
            $path = Join-Path $at "tools/$name"
            $extension = [IO.Path]::GetExtension($name)
            $runner = switch ($extension) { '.py' { Find-Python } '.sh' { Find-Sh } default { (Get-Process -Id $PID).Path } }
            if (-not $runner) { $refusal = "No working $(if ($extension -eq '.py') { 'python' } else { 'sh' }) to run tools/$name, which this PR's tool change needs. Install it and run this again."; break }
            $arguments = @(if ($extension -eq '.ps1') { '-NoProfile', '-File', $path } else { $path })
            Write-Land "Running tools/$name..."
            $global:LASTEXITCODE = 0
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            Push-Location $at
            try { $output = & $runner @arguments 2>&1 | Out-String }
            finally { Pop-Location; $ErrorActionPreference = $previous }
            if ($LASTEXITCODE -ne 0) {
                $tail = (($output.TrimEnd() -split "`r?`n") | Select-Object -Last 15) -join "`n    "
                $failed += "tools/$name (exit $LASTEXITCODE):`n    $tail"
            }
        }
        if ($refusal) { }
        elseif ($failed.Count) {
            $refusal = "PR #$number changes a tool whose test fails (knowledge/testing.md#tool-tests):`n  " + ($failed -join "`n  ") + "`nFix the tool or its test, push, then run this again."
        }
        else { Write-Land "Tool tests passed: $($tests -join ', ')." }
    }
    finally {
        if ($temporary) {
            [void](Invoke-RepoGit @('worktree', 'remove', '--force', $temporary))
            if (Test-Path -LiteralPath $temporary) { Write-Land "Left the temporary worktree $temporary (git worktree remove failed); delete it with tools/remove-worktree.ps1." 'Yellow' }
        }
    }
    if ($refusal) { Stop-Land $refusal }
}

$pull = Get-PullRequest $Pr
$number = $pull.number
$head = $pull.headRefName
$headSha = $pull.headRefOid

if ($pull.baseRefName -ne $AutodevBranch) {
    Stop-Land "PR #$number targets '$($pull.baseRefName)'. Claude only merges PRs into $AutodevBranch; the user merges everything else, including every PR into master."
}

if ($pull.state -eq 'OPEN') {
    $local = Get-LocalSha $head
    if ($local -and $local -ne $headSha) {
        Stop-Land "Local $head is at $($local.Substring(0, 10)) but PR #$number's head is $($headSha.Substring(0, 10)). Push the branch (or pull it) so the PR holds exactly what was tested, then run this again."
    }
    $owner = Get-CheckedOutWorktree $head
    if ($owner) {
        $dirty = @(((Invoke-RepoGit @('status', '--porcelain', '--untracked-files=no') $owner).Text -split "`r?`n") | Where-Object { $_ })
        if ($dirty.Count) {
            Stop-Land ("$owner has uncommitted changes on $($head):`n  " + ($dirty -join "`n  ") + "`nCommit and push them (or drop them), then run this again.")
        }
    }
    if ($pull.mergeable -eq 'CONFLICTING') {
        Stop-Land "PR #$number conflicts with $AutodevBranch. Merge origin/$AutodevBranch into $head, re-test what the merge touched, push, then run this again."
    }
    Test-MergedBudget
    Test-ToolTests

    Write-Land "Squash-merging PR #$number ($head at $($headSha.Substring(0, 10))) into $AutodevBranch..."
    $merge = Invoke-Gh @('pr', 'merge', "$number", '--squash', '--match-head-commit', $headSha)
    if ($merge.ExitCode -ne 0) { Stop-Land "The merge failed: $($merge.Text)" 1 }
    $pull = Get-PullRequest "$number"
}
elseif ($pull.state -eq 'MERGED') {
    Write-Land "PR #$number is already merged; finishing the cleanup."
}
else {
    Stop-Land "PR #$number is $($pull.state), not open."
}

$failed = $false
$squash = if ($pull.mergeCommit -and $pull.mergeCommit.oid) { $pull.mergeCommit.oid.Substring(0, 10) } else { '?' }

[void](Invoke-RepoGit @('fetch', '--quiet', '--prune', 'origin'))
if ((Invoke-RepoGit @('rev-parse', '--verify', '--quiet', "refs/remotes/origin/$head")).ExitCode -eq 0) {
    $delete = Invoke-RepoGit @('push', '--quiet', 'origin', '--delete', $head)
    if ($delete.ExitCode -eq 0) { Write-Land "Deleted origin/$head." }
    else { Write-Land "Couldn't delete origin/$($head): $($delete.Error)" 'Yellow'; $failed = $true }
}

if (Get-LocalSha $head) {
    $owner = Get-CheckedOutWorktree $head
    if ($owner -and (Test-SamePath $owner $repo)) {
        $detach = Invoke-RepoGit @('switch', '--quiet', '--detach')
        if ($detach.ExitCode -ne 0) { Write-Land "Couldn't detach this checkout from $($head): $($detach.Error)" 'Yellow'; $failed = $true; $owner = $repo }
        else { Write-Land "Detached this checkout (same commit) so $head can go."; $owner = $null }
    }
    if ($owner) {
        Write-Land "Left local ${head}: it is checked out in $owner. tools/prune-worktrees.ps1 removes that worktree and the branch once it is idle."
    }
    else {
        $branchDelete = Invoke-RepoGit @('branch', '-D', $head)
        if ($branchDelete.ExitCode -eq 0) { Write-Land "Deleted local $head." }
        else { Write-Land "Couldn't delete local $($head): $($branchDelete.Error)" 'Yellow'; $failed = $true }
    }
}

if ($failed) { Write-Land "PR #$number landed in $AutodevBranch as $squash, but a cleanup step failed (above)." 'Yellow'; exit 1 }
Write-Land "Landed PR #$number ($head) in $AutodevBranch as $squash." 'Green'
exit 0
