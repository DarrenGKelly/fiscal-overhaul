<#
.SYNOPSIS
    Tests tools/land-pr.ps1 against a throwaway repo and a stub gh.

.DESCRIPTION
    No network: a bare origin under TEMP, and a stub gh (a PowerShell script) that answers 'pr view' from a JSON
    state file and records 'pr merge' calls. Each landing has its refusal beside it: a PR into master, an
    unpushed local commit, uncommitted work, a conflicting PR, a merge with autodev that puts a doc over its
    size budget, a changed tool whose tools/test-<stem> fails (in the branch's worktree or a temporary one), a
    machine with no python to check the budget (landed only with -SkipBudget). Run after changing the script:
        ./tools/test-land-pr.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$landScript = Join-Path $toolsDir 'land-pr.ps1'
. (Join-Path $toolsDir 'git-common.ps1')

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

function Git([string]$Path, [string[]]$Arguments) { (Invoke-Git -Path $Path -Arguments $Arguments).Text }
function Write-File([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-land-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$origin = Join-Path $root 'origin.git'
$repo = Join-Path $root 'repo'
$statePath = Join-Path $root 'gh-state.json'
$logPath = Join-Path $root 'gh-log.txt'
$stub = Join-Path $root 'gh-stub.ps1'

# The stub: 'pr view <selector>' prints the matching PR (by number or head branch); 'pr merge <n>' marks it merged.
$stubCode = @'
$state = Get-Content -Raw -LiteralPath $env:FO_GH_STATE | ConvertFrom-Json
Add-Content -LiteralPath $env:FO_GH_LOG -Value ($args -join ' ')
$selector = "$($args[2])"
$pull = $state.prs | Where-Object { "$($_.number)" -eq $selector -or $_.headRefName -eq $selector } | Select-Object -First 1
if (-not $pull) { [Console]::Error.WriteLine("no pull requests found for $selector"); exit 1 }
if ($args[1] -eq 'view') { $pull | ConvertTo-Json -Depth 5; exit 0 }
if ($args[1] -eq 'merge') {
    $pull.state = 'MERGED'
    $pull.mergeCommit = [pscustomobject]@{ oid = 'abcdef0123456789abcdef0123456789abcdef01' }
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $env:FO_GH_STATE
    exit 0
}
exit 1
'@

function Set-Prs([object[]]$Prs) {
    @{ prs = $Prs } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath
    Set-Content -LiteralPath $logPath -Value ''
}

function New-Pr([int]$Number, [string]$Head, [string]$Base = 'autodev', [string]$Mergeable = 'MERGEABLE') {
    return [pscustomobject]@{ number = $Number; state = 'OPEN'; baseRefName = $Base; headRefName = $Head
        headRefOid = (Git $repo @('rev-parse', "origin/$Head")); mergeable = $Mergeable; mergeCommit = $null }
}

function Invoke-Land([string]$At, [string]$Selector, [switch]$SkipBudget) {
    $global:LASTEXITCODE = 0
    Push-Location $At
    try { $output = & $landScript $Selector -GhPath $stub -SkipBudget:$SkipBudget *>&1 | Out-String }
    finally { Pop-Location }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Log = (Get-Content -Raw -LiteralPath $logPath) }
}

function New-Branch([string]$Name, [string]$File, [string]$Worktree = '') {
    if ($Worktree) {
        Git $repo @('worktree', 'add', '--quiet', '-b', $Name, $Worktree, 'origin/autodev') | Out-Null
        $at = $Worktree
    }
    else {
        Git $repo @('branch', $Name, 'origin/autodev') | Out-Null
        $at = Join-Path $root "tmp-$($Name -replace '/', '-')"
        Git $repo @('worktree', 'add', '--quiet', $at, $Name) | Out-Null
    }
    Write-File (Join-Path $at $File) "$File`n"
    Git $at @('add', '--all') | Out-Null
    Git $at @('commit', '--quiet', '--no-verify', '-m', $File) | Out-Null
    Git $at @('push', '--quiet', 'origin', $Name) | Out-Null
    if (-not $Worktree) { Git $repo @('worktree', 'remove', $at) | Out-Null }
}

# Doc text of `$Count` 100-byte lines (LF), each starting with `$Tag`, so two branches can edit the same doc apart.
function Get-DocLines([string]$Tag, [int]$Count) {
    $line = ($Tag + ' ').PadRight(99, 'x')
    return (@(1..$Count | ForEach-Object { $line }) -join "`n") + "`n"
}

# A branch off origin/autodev that writes knowledge/<File> with $Text, pushed.
function New-DocBranch([string]$Name, [string]$File, [string]$Text) {
    Git $repo @('branch', $Name, 'origin/autodev') | Out-Null
    $at = Join-Path $root "tmp-$($Name -replace '/', '-')"
    Git $repo @('worktree', 'add', '--quiet', $at, $Name) | Out-Null
    Write-File (Join-Path $at "knowledge/$File") $Text
    Git $at @('add', '--all') | Out-Null
    Git $at @('commit', '--quiet', '--no-verify', '-m', $File) | Out-Null
    Git $at @('push', '--quiet', 'origin', $Name) | Out-Null
    Git $repo @('worktree', 'remove', $at) | Out-Null
}

function Test-Remote([string]$Branch) { return [bool](Git $repo @('ls-remote', '--heads', 'origin', $Branch)) }
function Test-Local([string]$Branch) { return [bool](Git $repo @('branch', '--list', $Branch)) }

try {
    New-Item -ItemType Directory -Force $repo | Out-Null
    Set-Content -LiteralPath $stub -Value $stubCode
    $env:FO_GH_STATE = $statePath
    $env:FO_GH_LOG = $logPath
    Git $root @('init', '--quiet', '--bare', $origin) | Out-Null
    Git $repo @('init', '--quiet', '-b', 'master') | Out-Null
    Git $repo @('config', 'user.email', 'land@example.invalid') | Out-Null
    Git $repo @('config', 'user.name', 'land') | Out-Null
    Git $repo @('config', 'core.hooksPath', (Join-Path $repo 'no-hooks')) | Out-Null
    Write-File (Join-Path $repo '.gitignore') ".claude/`n"
    Write-File (Join-Path $repo 'readme.txt') "base`n"
    # The merged-tree budget check runs the tree's own tools/okf.py budget-tree on knowledge/.
    Copy-Item -LiteralPath (Join-Path $toolsDir 'okf.py') (New-Item -ItemType Directory -Force (Join-Path $repo 'tools')).FullName
    Write-File (Join-Path $repo 'knowledge/big.md') (Get-DocLines 'base' 340)
    Git $repo @('add', '--all') | Out-Null
    Git $repo @('commit', '--quiet', '--no-verify', '-m', 'base') | Out-Null
    Git $repo @('remote', 'add', 'origin', $origin) | Out-Null
    Git $repo @('push', '--quiet', 'origin', 'master', 'master:autodev') | Out-Null
    Git $repo @('fetch', '--quiet', 'origin') | Out-Null

    # -----------------------------------------------------------------------
    # Pass: a session lands its own branch from the worktree that has it checked out
    # -----------------------------------------------------------------------
    $own = Join-Path $repo '.claude/worktrees/own'
    New-Branch 'feature/own' 'own.txt' $own
    $ownSha = Git $own @('rev-parse', 'HEAD')
    Set-Prs @(New-Pr 10 'feature/own')
    $landed = Invoke-Land $own '10'
    Check ($landed.ExitCode -eq 0 -and $landed.Output -match 'Landed PR #10') 'lands an open PR into autodev and exits 0' $landed.Output
    Check ($landed.Log -match "pr merge 10 --squash --match-head-commit $ownSha") '... squash-merging at the head sha it checked' $landed.Log
    Check ($landed.Log -notmatch 'delete-branch') '... without gh''s --delete-branch' $landed.Log
    Check (-not (Test-Remote 'feature/own')) '... deletes the remote branch' $landed.Output
    Check (-not (Test-Local 'feature/own')) '... and the local one, though this worktree had it checked out' $landed.Output
    Check ((Git $own @('rev-parse', 'HEAD')) -eq $ownSha -and (Invoke-Git -Path $own -Arguments @('symbolic-ref', '--quiet', 'HEAD') -AllowFailure).ExitCode -ne 0) '... leaving the worktree detached at the same commit' $landed.Output

    Set-Content -LiteralPath $logPath -Value ''
    $again = Invoke-Land $own '10'
    Check ($again.ExitCode -eq 0 -and $again.Output -match 'already merged' -and $again.Log -notmatch 'pr merge') 'a second run on a merged PR only finishes the cleanup' $again.Output

    # -----------------------------------------------------------------------
    # Pass: an orchestrator lands a child's branch by name; the child's worktree is left alone
    # -----------------------------------------------------------------------
    $child = Join-Path $repo '.claude/worktrees/child'
    New-Branch 'feature/child' 'child.txt' $child
    Set-Prs @(New-Pr 11 'feature/child')
    $byName = Invoke-Land $repo 'feature/child'
    Check ($byName.ExitCode -eq 0 -and $byName.Log -match 'pr merge 11 ') 'lands a PR given by branch name' $byName.Output
    Check ((Test-Local 'feature/child') -and (Test-Path $child) -and $byName.Output -match 'checked out in') '... leaving a branch another worktree has checked out, and saying so' $byName.Output

    # -----------------------------------------------------------------------
    # Fail: refusals merge nothing
    # -----------------------------------------------------------------------
    New-Branch 'feature/promote' 'promote.txt'
    Set-Prs @(New-Pr 12 'feature/promote' 'master')
    $master = Invoke-Land $repo '12'
    Check ($master.ExitCode -eq 2 -and $master.Output -match "targets 'master'" -and $master.Log -notmatch 'pr merge') 'refuses a PR into master and merges nothing' $master.Output
    Check (Test-Remote 'feature/promote') '... and deletes nothing' $master.Output

    $ahead = Join-Path $repo '.claude/worktrees/ahead'
    New-Branch 'feature/ahead' 'ahead.txt' $ahead
    Set-Prs @(New-Pr 13 'feature/ahead')
    Write-File (Join-Path $ahead 'late.txt') "late`n"
    Git $ahead @('add', '--all') | Out-Null
    Git $ahead @('commit', '--quiet', '--no-verify', '-m', 'late') | Out-Null
    $unpushed = Invoke-Land $ahead '13'
    Check ($unpushed.ExitCode -eq 2 -and $unpushed.Output -match 'Push the branch' -and $unpushed.Log -notmatch 'pr merge') 'refuses when the local branch has a commit the PR lacks' $unpushed.Output

    $dirty = Join-Path $repo '.claude/worktrees/dirty'
    New-Branch 'feature/dirty' 'dirty.txt' $dirty
    Set-Prs @(New-Pr 14 'feature/dirty')
    Write-File (Join-Path $dirty 'dirty.txt') "edited`n"
    $uncommitted = Invoke-Land $repo 'feature/dirty'
    Check ($uncommitted.ExitCode -eq 2 -and $uncommitted.Output -match 'uncommitted changes' -and $uncommitted.Log -notmatch 'pr merge') 'refuses when the branch''s worktree has uncommitted changes' $uncommitted.Output

    New-Branch 'feature/clash' 'clash.txt'
    Set-Prs @(New-Pr 15 'feature/clash' 'autodev' 'CONFLICTING')
    $conflicting = Invoke-Land $repo '15'
    Check ($conflicting.ExitCode -eq 2 -and $conflicting.Output -match 'conflicts with autodev' -and $conflicting.Log -notmatch 'pr merge') 'refuses a PR GitHub calls conflicting' $conflicting.Output

    $closed = New-Pr 16 'feature/clash'
    $closed.state = 'CLOSED'
    Set-Prs @($closed)
    $notOpen = Invoke-Land $repo '16'
    Check ($notOpen.ExitCode -eq 2 -and $notOpen.Output -match 'CLOSED') 'refuses a closed PR' $notOpen.Output

    # -----------------------------------------------------------------------
    # Doc budgets on the merged tree: two branches that each fit big.md's 40 KB budget (34 KB + 6 KB each) but
    # overflow it together (46 KB). The first lands (pushed straight to autodev here, as the stub merges nothing);
    # the second is refused. A branch editing another doc still lands.
    # -----------------------------------------------------------------------
    $base = Get-DocLines 'base' 340
    New-DocBranch 'feature/top' 'big.md' ((Get-DocLines 'top' 60) + $base)
    New-DocBranch 'feature/bottom' 'big.md' ($base + (Get-DocLines 'bottom' 60))
    New-DocBranch 'feature/other' 'other.md' (Get-DocLines 'other' 10)
    Git $repo @('push', '--quiet', 'origin', 'refs/remotes/origin/feature/top:refs/heads/autodev') | Out-Null
    Set-Prs @(New-Pr 17 'feature/bottom')
    $overflow = Invoke-Land $repo '17'
    Check ($overflow.ExitCode -eq 2 -and $overflow.Output -match 'merge origin/autodev' -and $overflow.Output -match 'trim, or split' -and $overflow.Output -match 'knowledge/big.md' -and $overflow.Log -notmatch 'pr merge') 'refuses a PR whose merge with autodev puts a doc over its budget, naming it' $overflow.Output
    Check (Test-Remote 'feature/bottom') '... and deletes nothing' $overflow.Output

    Set-Prs @(New-Pr 18 'feature/other')
    $disjoint = Invoke-Land $repo '18'
    Check ($disjoint.ExitCode -eq 0 -and $disjoint.Output -match 'Doc budgets fit' -and $disjoint.Log -match 'pr merge 18 ') 'lands a PR editing another doc (the merged tree fits)' $disjoint.Output

    # -----------------------------------------------------------------------
    # Tool tests at landing (knowledge/testing.md#tool-tests): a PR that changes tools/okf.py runs tools/test-okf.py from
    # the PR head and is refused when it fails; a passing one lands. Both in the branch's own worktree and, when no
    # checkout has the branch, in a temporary worktree that is removed afterwards. A .githooks/ change runs
    # tools/test-git-hooks.sh; a tool with no test of its own runs nothing.
    # -----------------------------------------------------------------------
    $failingTest = "import sys`nprint('stub test-okf: 1 failure(s)')`nsys.exit(1)`n"
    $passingTest = "print('stub test-okf: 0 failure(s)')`n"
    function New-ToolBranch([string]$Name, [hashtable]$Files, [string]$Worktree = '') {
        if ($Worktree) { Git $repo @('worktree', 'add', '--quiet', '-b', $Name, $Worktree, 'origin/autodev') | Out-Null; $at = $Worktree }
        else {
            Git $repo @('branch', $Name, 'origin/autodev') | Out-Null
            $at = Join-Path $root "tmp-$($Name -replace '/', '-')"
            Git $repo @('worktree', 'add', '--quiet', $at, $Name) | Out-Null
        }
        foreach ($file in $Files.Keys) {
            $path = Join-Path $at $file
            if ($file -eq 'tools/okf.py') { Add-Content -LiteralPath $path -Value "# $Name" }
            else { Write-File $path $Files[$file] }
        }
        Git $at @('add', '--all') | Out-Null
        Git $at @('commit', '--quiet', '--no-verify', '-m', $Name) | Out-Null
        Git $at @('push', '--quiet', 'origin', $Name) | Out-Null
        if (-not $Worktree) { Git $repo @('worktree', 'remove', $at) | Out-Null }
    }

    $redTools = Join-Path $repo '.claude/worktrees/red-tools'
    New-ToolBranch 'feature/red-tools' @{ 'tools/okf.py' = ''; 'tools/test-okf.py' = $failingTest } $redTools
    Set-Prs @(New-Pr 30 'feature/red-tools')
    $red = Invoke-Land $redTools '30'
    Check ($red.ExitCode -eq 2 -and $red.Output -match 'tool whose test fails' -and $red.Output -match 'tools/test-okf\.py' -and $red.Output -match 'stub test-okf: 1 failure' -and $red.Log -notmatch 'pr merge') 'refuses a PR that changes tools/okf.py when its test-okf.py fails, quoting the failure, and merges nothing' $red.Output
    Check (Test-Remote 'feature/red-tools') '... and deletes nothing' $red.Output

    $greenTools = Join-Path $repo '.claude/worktrees/green-tools'
    New-ToolBranch 'feature/green-tools' @{ 'tools/okf.py' = ''; 'tools/test-okf.py' = $passingTest } $greenTools
    Set-Prs @(New-Pr 31 'feature/green-tools')
    $green = Invoke-Land $greenTools '31'
    Check ($green.ExitCode -eq 0 -and $green.Output -match 'Tool tests passed: test-okf\.py' -and $green.Log -match 'pr merge 31 ') 'lands a PR that changes tools/okf.py when its test-okf.py passes' $green.Output

    New-ToolBranch 'feature/red-unchecked' @{ 'tools/okf.py' = ''; 'tools/test-okf.py' = $failingTest }
    Set-Prs @(New-Pr 32 'feature/red-unchecked')
    $redTemp = Invoke-Land $repo '32'
    Check ($redTemp.ExitCode -eq 2 -and $redTemp.Output -match 'temporary worktree' -and $redTemp.Output -match 'tool whose test fails' -and $redTemp.Output -match 'stub test-okf: 1 failure' -and $redTemp.Log -notmatch 'pr merge') 'with no checkout of the branch, runs the tool tests in a temporary worktree and refuses a failure' $redTemp.Output

    New-ToolBranch 'feature/green-unchecked' @{ 'tools/okf.py' = ''; 'tools/test-okf.py' = $passingTest }
    Set-Prs @(New-Pr 33 'feature/green-unchecked')
    $greenTemp = Invoke-Land $repo '33'
    Check ($greenTemp.ExitCode -eq 0 -and $greenTemp.Output -match 'Tool tests passed' -and $greenTemp.Log -match 'pr merge 33 ') '... and lands a pass' $greenTemp.Output
    $worktrees = Git $repo @('worktree', 'list', '--porcelain')
    Check ($worktrees -notmatch 'fo-land-tools-') '... removing the temporary worktree both times' $worktrees

    New-ToolBranch 'feature/red-hooks' @{ '.githooks/pre-commit' = "#!/bin/sh`nexit 0`n"; 'tools/test-git-hooks.sh' = "echo 'stub hooks: 2 failed'`nexit 1`n" }
    Set-Prs @(New-Pr 34 'feature/red-hooks')
    $hooks = Invoke-Land $repo '34'
    Check ($hooks.ExitCode -eq 2 -and $hooks.Output -match 'tools/test-git-hooks\.sh' -and $hooks.Output -match 'stub hooks: 2 failed' -and $hooks.Log -notmatch 'pr merge') 'a .githooks/ change runs tools/test-git-hooks.sh and refuses its failure' $hooks.Output

    New-ToolBranch 'feature/untested-tool' @{ 'tools/no-test-tool.py' = "print('x')`n"; 'tools/test-okf.py' = $failingTest.Replace('okf', 'unrelated') }
    Set-Prs @(New-Pr 35 'feature/untested-tool')
    $untested = Invoke-Land $repo '35'
    Check ($untested.ExitCode -eq 2 -and $untested.Output -match 'tools/test-okf\.py' -and $untested.Output -match 'stub test-unrelated: 1 failure') 'control: a changed test is run itself (a failing test-okf.py refuses on its own)' $untested.Output
    New-ToolBranch 'feature/plain-tool' @{ 'tools/no-test-tool.py' = "print('x')`n" }
    Set-Prs @(New-Pr 36 'feature/plain-tool')
    $plain = Invoke-Land $repo '36'
    Check ($plain.ExitCode -eq 0 -and $plain.Output -notmatch 'Running tools/' -and $plain.Log -match 'pr merge 36 ') 'a tool with no test of its own lands without running one' $plain.Output

    # -----------------------------------------------------------------------
    # No working python: the budget check can't run, so land-pr refuses (exit 2) rather than skipping it, unless
    # the user passes -SkipBudget. A python3 and python that exist but fail, first on PATH, hide the real
    # ones (Find-Python takes the first of each name, as with Windows' Store stub).
    # -----------------------------------------------------------------------
    New-DocBranch 'feature/no-python' 'third.md' (Get-DocLines 'third' 10)
    Set-Prs @(New-Pr 19 'feature/no-python')
    $fakeBin = Join-Path $root 'no-python'
    New-Item -ItemType Directory -Force $fakeBin | Out-Null
    foreach ($name in 'python3', 'python') {
        Set-Content -LiteralPath (Join-Path $fakeBin "$name.cmd") -Value "@exit /b 1"
    }
    $savedPath = $env:PATH
    $env:PATH = $fakeBin + [IO.Path]::PathSeparator + $env:PATH
    try {
        $noPython = Invoke-Land $repo '19'
        $skipped = Invoke-Land $repo '19' -SkipBudget
    }
    finally { $env:PATH = $savedPath }
    Check ($noPython.ExitCode -eq 2 -and $noPython.Output -match 'No working python' -and $noPython.Output -match '-SkipBudget' -and $noPython.Log -notmatch 'pr merge') 'refuses to land when no python can run the merged-tree budget check (exit 2, merges nothing)' $noPython.Output
    Check ($skipped.ExitCode -eq 0 -and $skipped.Output -match 'SkipBudget skips' -and $skipped.Log -match 'pr merge 19 ') '... and lands with -SkipBudget, saying it skipped the check' $skipped.Output
}
finally {
    Set-Location $toolsDir
    Remove-Item Env:FO_GH_STATE, Env:FO_GH_LOG -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $root) {
        Get-ChildItem $root -Recurse -Force | ForEach-Object { $_.Attributes = 'Normal' }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failures) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All land-pr checks passed.'
exit 0
