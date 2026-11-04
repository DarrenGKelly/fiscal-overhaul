<#
.SYNOPSIS
    Tests tools/prune-worktrees.ps1 against a throwaway repo.

.DESCRIPTION
    Never touches this repo: it builds a repo with a bare origin under TEMP, a spread of worktrees in
    every state the sweep has to tell apart, and deletes it all afterwards. Each keep is the fail case of
    a remove: work that would be lost (dirty, unpushed), a live session (recent, locked, this shell's), and
    the junction target (a linked UI/node_modules) that must survive. Run after changing the script:
        ./tools/test-prune-worktrees.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$pruneScript = Join-Path $toolsDir 'prune-worktrees.ps1'
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

function Invoke-Prune([hashtable]$Arguments) {
    $global:LASTEXITCODE = 0
    $output = & $pruneScript @Arguments *>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-prune-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$origin = Join-Path $root 'origin.git'
$repo = Join-Path $root 'repo'
$junctionTarget = Join-Path $root 'node_modules'
$wtRoot = Join-Path $repo '.claude\worktrees'

function Add-Worktree([string]$Name, [string]$Branch = '', [string]$At = 'master') {
    $path = Join-Path $wtRoot $Name
    if ($Branch) { Git $repo @('worktree', 'add', '--quiet', '-b', $Branch, $path, $At) | Out-Null }
    else { Git $repo @('worktree', 'add', '--quiet', '--detach', $path, $At) | Out-Null }
    return $path
}

function Add-Commit([string]$Worktree, [string]$File) {
    Write-File (Join-Path $Worktree $File) "$File`n"
    Git $Worktree @('add', '--all') | Out-Null
    Git $Worktree @('commit', '--quiet', '--no-verify', '-m', $File) | Out-Null
    return Git $Worktree @('rev-parse', 'HEAD')
}

# Makes a worktree look idle: backdate what the sweep reads git activity from.
function Set-Idle([string]$Worktree, [double]$Hours = 48) {
    $gitDir = Git $Worktree @('rev-parse', '--absolute-git-dir')
    foreach ($name in 'HEAD', 'index', 'logs\HEAD') {
        $file = Join-Path $gitDir $name
        if (Test-Path -LiteralPath $file) { (Get-Item -LiteralPath $file -Force).LastWriteTimeUtc = [DateTime]::UtcNow.AddHours(-$Hours) }
    }
}

try {
    New-Item -ItemType Directory -Force $repo, $junctionTarget | Out-Null
    Git $root @('init', '--quiet', '--bare', $origin) | Out-Null
    Git $repo @('init', '--quiet', '-b', 'master') | Out-Null
    Git $repo @('config', 'user.email', 'prune@example.invalid') | Out-Null
    Git $repo @('config', 'user.name', 'prune') | Out-Null
    Git $repo @('config', 'core.hooksPath', (Join-Path $repo 'no-hooks')) | Out-Null
    Git $repo @('config', 'core.autocrlf', 'false') | Out-Null
    Write-File (Join-Path $repo '.gitignore') ".claude/`nnode_modules/`n"
    Write-File (Join-Path $repo 'readme.txt') "base`n"
    Git $repo @('add', '--all') | Out-Null
    Git $repo @('commit', '--quiet', '--no-verify', '-m', 'base') | Out-Null
    Git $repo @('remote', 'add', 'origin', $origin) | Out-Null
    Git $repo @('push', '--quiet', 'origin', 'master', 'master:autodev') | Out-Null
    Git $repo @('fetch', '--quiet', 'origin') | Out-Null

    # Removable: squash-merged PR head (not an ancestor of anything), pushed-only, detached at a landed commit.
    $merged = Add-Worktree 'merged' 'feature/merged'
    $mergedSha = Add-Commit $merged 'merged.txt'
    Git $merged @('push', '--quiet', 'origin', 'feature/merged') | Out-Null
    $pushed = Add-Worktree 'pushed' 'fix/pushed'
    Add-Commit $pushed 'pushed.txt' | Out-Null
    Git $pushed @('push', '--quiet', 'origin', 'fix/pushed') | Out-Null
    $detached = Add-Worktree 'detached'

    # Removable, with a UI/node_modules junction into the main checkout's that must survive the removal.
    $junctioned = Add-Worktree 'junctioned'
    Write-File (Join-Path $junctionTarget 'react.js') "precious`n"
    New-Item -ItemType Junction -Path (Join-Path $junctioned 'UI\node_modules') -Target $junctionTarget | Out-Null

    # Kept: work that would be lost.
    $dirty = Add-Worktree 'dirty' 'feature/dirty'
    Write-File (Join-Path $dirty 'notes.txt') "unsaved`n"
    $unpushed = Add-Worktree 'unpushed' 'feature/unpushed'
    Add-Commit $unpushed 'unpushed.txt' | Out-Null

    # Kept: live sessions.
    $fresh = Add-Worktree 'fresh' 'feature/fresh'
    $locked = Add-Worktree 'locked' 'feature/locked'
    Git $repo @('worktree', 'lock', $locked) | Out-Null
    $shell = Add-Worktree 'shell' 'feature/shell'

    # A test batch idle for 2 h: a short sweep keeps it (another orchestrator's agent may be about to use it);
    # one idle for 2 days goes.
    $batch = Add-Worktree 'batch-live' 'feature/batch-live'
    Git $batch @('push', '--quiet', 'origin', 'feature/batch-live') | Out-Null
    $oldBatch = Add-Worktree 'batch-old' 'feature/batch-old'
    Git $oldBatch @('push', '--quiet', 'origin', 'feature/batch-old') | Out-Null

    # Branches with no worktree: one merged (fast-forward), one not, and the protected ones.
    Git $repo @('branch', 'claude/orphan-merged', 'master') | Out-Null
    $orphan = Add-Worktree 'orphan-tmp' 'feature/orphan-unmerged'
    Add-Commit $orphan 'orphan.txt' | Out-Null
    Git $repo @('worktree', 'remove', $orphan) | Out-Null
    Git $repo @('branch', 'autodev', 'master') | Out-Null

    # Removable: a subagent's worktree two commits behind the head its PR merged at (a fix session moved it on).
    $behind = Add-Worktree 'behind' '' 'master'
    Add-Commit $behind 'behind.txt' | Out-Null
    $behindSha = Git $behind @('rev-parse', 'HEAD')
    $mover = Add-Worktree 'mover-tmp' 'feature/moved-on' $behindSha
    $movedHead = Add-Commit $mover 'fix.txt'
    Git $repo @('worktree', 'remove', $mover) | Out-Null

    # Claude Code's subagent locks: removable once the locking process has gone; kept while it runs.
    $staleLocked = Add-Worktree 'stale-lock'
    Git $repo @('worktree', 'lock', '--reason', 'claude agent agent-gone (pid 2147480000)', $staleLocked) | Out-Null
    $liveLocked = Add-Worktree 'live-lock'
    Git $repo @('worktree', 'lock', '--reason', "claude agent agent-live (pid $PID)", $liveLocked) | Out-Null

    # An orchestrator's worktree whose only changes are its ledger and captures: archived and removed once
    # idle past the ledger's 72 h; kept before that, and kept when anything else is dirty.
    $ledgerOld = Add-Worktree 'ledger-old'
    Write-File (Join-Path $ledgerOld 'tasks\batch-a\ledger.md') "ledger`n"
    Write-File (Join-Path $ledgerOld 'captures-pass2\shot.txt') "png`n"
    # Older branches track tasks/.gitkeep: only the untracked batch folder may move, or the worktree goes dirty.
    $ledgerKept = Add-Worktree 'ledger-gitkeep' 'feature/ledger-gitkeep'
    Add-Commit $ledgerKept 'tasks\.gitkeep' | Out-Null
    Git $ledgerKept @('push', '--quiet', 'origin', 'feature/ledger-gitkeep') | Out-Null
    Write-File (Join-Path $ledgerKept 'tasks\batch-d\ledger.md') "ledger`n"
    $ledgerNew = Add-Worktree 'ledger-new'
    Write-File (Join-Path $ledgerNew 'tasks\batch-b\ledger.md') "ledger`n"
    $ledgerMixed = Add-Worktree 'ledger-mixed'
    Write-File (Join-Path $ledgerMixed 'tasks\batch-c\ledger.md') "ledger`n"
    Write-File (Join-Path $ledgerMixed 'work.txt') "unsaved`n"

    # Orphan folders git no longer lists: one holding only a junction and an empty folder is cleared (the
    # junction's target survives); one holding a real file is left alone.
    $orphanLinks = Join-Path $wtRoot 'orphan-links'
    New-Item -ItemType Directory -Force (Join-Path $orphanLinks 'UI\Empty') | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $orphanLinks 'UI\node_modules') -Target $junctionTarget | Out-Null
    $orphanFiles = Join-Path $wtRoot 'orphan-files'
    Write-File (Join-Path $orphanFiles 'UI\left.txt') "left`n"
    # An empty orphan another process has as its working directory: reported as kept, not a failure.
    $orphanBusy = Join-Path $wtRoot 'orphan-busy'
    New-Item -ItemType Directory -Force $orphanBusy | Out-Null
    $squatter = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', 'Start-Sleep 120' -WorkingDirectory $orphanBusy -WindowStyle Hidden -PassThru

    foreach ($wt in $merged, $pushed, $detached, $junctioned, $dirty, $unpushed, $locked, $shell, $oldBatch, $behind, $staleLocked, $liveLocked, $ledgerMixed) { Set-Idle $wt }
    Set-Idle $batch 2
    Set-Idle $ledgerOld 100
    Set-Idle $ledgerKept 100
    Set-Idle $ledgerNew 48

    $arguments = @{ Path = $repo; MinIdleHours = 1; MergedHead = @($mergedSha, $movedHead) }

    # -----------------------------------------------------------------------
    # Dry run: reports, changes nothing
    # -----------------------------------------------------------------------
    Push-Location $shell
    try { $dry = Invoke-Prune $arguments }
    finally { Pop-Location }
    Check ($dry.ExitCode -eq 0) 'a dry run exits 0' $dry.Output
    Check ($dry.Output -match 'remove\s+merged' -and $dry.Output -match 'delete\s+branch feature/merged') 'a dry run lists the merged worktree and its branch' $dry.Output
    Check ((Test-Path $merged) -and (Git $repo @('branch', '--list', 'feature/merged'))) 'a dry run leaves the worktree and branch in place' $dry.Output
    Check ((Test-Path (Join-Path $ledgerOld 'tasks\batch-a\ledger.md')) -and (Test-Path (Join-Path $orphanLinks 'UI\node_modules'))) 'a dry run moves no ledger and unlinks no orphan' $dry.Output

    # -----------------------------------------------------------------------
    # Apply
    # -----------------------------------------------------------------------
    Push-Location $shell
    try { $run = Invoke-Prune ($arguments + @{ Apply = $true }) }
    finally { Pop-Location }
    Check ($run.ExitCode -eq 0) '-Apply exits 0' $run.Output

    $listed = Git $repo @('worktree', 'list')
    function Test-Listed([string]$Path) { return $listed -match ('[\\/]worktrees[\\/]' + [regex]::Escape((Split-Path -Leaf $Path)) + '\s') }
    function Test-Branch([string]$Name) { return [bool](Git $repo @('branch', '--list', $Name)) }

    Check (-not (Test-Listed $merged) -and -not (Test-Path $merged)) 'removes a worktree whose HEAD is a merged PR head' $run.Output
    Check (-not (Test-Branch 'feature/merged')) '... and deletes its squash-merged branch' $run.Output
    Check (-not (Test-Listed $pushed)) 'removes a worktree whose HEAD is pushed' $run.Output
    Check (Test-Branch 'fix/pushed') '... but keeps a branch that is only pushed, not merged' $run.Output
    Check (-not (Test-Listed $detached)) 'removes a detached worktree at a landed commit' $run.Output
    Check (-not (Test-Listed $junctioned)) 'removes a worktree holding a node_modules junction' $run.Output
    Check (Test-Path (Join-Path $junctionTarget 'react.js')) '... without touching what the junction points at' $run.Output

    Check ((Test-Listed $dirty) -and (Test-Path (Join-Path $dirty 'notes.txt')) -and $run.Output -match 'dirty.*uncommitted') 'keeps a worktree with uncommitted changes' $run.Output
    Check ((Test-Listed $unpushed) -and (Test-Branch 'feature/unpushed') -and $run.Output -match 'unpushed.*not on any remote') 'keeps a worktree with unpushed commits' $run.Output
    Check ((Test-Listed $fresh) -and (Test-Branch 'feature/fresh') -and $run.Output -match 'fresh.*active') 'keeps a clean, landed worktree git touched recently (a live session)' $run.Output
    Check ((Test-Listed $locked) -and $run.Output -match 'locked.*locked') 'keeps a locked worktree' $run.Output
    Check ((Test-Listed $shell) -and $run.Output -match 'shell.*this shell') 'keeps the worktree the shell is in' $run.Output

    Check ((Test-Listed $batch) -and $run.Output -match 'batch-live.*test batch keeps 12') 'keeps a test batch idle 2 h under -MinIdleHours 1, and says why' $run.Output
    Check (-not (Test-Listed $oldBatch)) 'removes a landed test batch idle past 12 h' $run.Output

    Check (-not (Test-Listed $behind)) 'removes a worktree whose HEAD is behind a merged PR head (contained in it)' $run.Output

    Check (-not (Test-Listed $staleLocked)) 'removes a worktree whose Claude agent lock outlived its process' $run.Output
    Check ((Test-Listed $liveLocked) -and $run.Output -match 'live-lock.*locked') 'keeps a worktree whose Claude agent lock''s process still runs' $run.Output

    $archived = Join-Path $repo '.claude\worktree-archive\ledger-old'
    Check (-not (Test-Listed $ledgerOld)) 'removes an orchestrator worktree idle past 72 h whose only changes are its ledger and captures' $run.Output
    Check ((Test-Path (Join-Path $archived 'tasks\batch-a\ledger.md')) -and (Test-Path (Join-Path $archived 'captures-pass2\shot.txt'))) '... after archiving the ledger and captures' $run.Output
    $archivedKept = Join-Path $repo '.claude\worktree-archive\ledger-gitkeep'
    Check (-not (Test-Listed $ledgerKept) -and (Test-Path (Join-Path $archivedKept 'tasks\batch-d\ledger.md')) -and -not (Test-Path (Join-Path $archivedKept 'tasks\.gitkeep'))) 'archives only the untracked batch folder when the branch tracks tasks/.gitkeep, and removes the worktree' $run.Output
    Check ((Test-Listed $ledgerNew) -and (Test-Path (Join-Path $ledgerNew 'tasks\batch-b\ledger.md')) -and $run.Output -match 'ledger-new.*ledger keeps 72') 'keeps an orchestrator worktree idle 48 h, ledger in place' $run.Output
    Check ((Test-Listed $ledgerMixed) -and $run.Output -match 'ledger-mixed.*uncommitted') 'keeps a worktree with a ledger and other changes' $run.Output

    Check (-not (Test-Path $orphanLinks)) 'clears an orphan folder holding only a junction and empty folders' $run.Output
    Check (Test-Path (Join-Path $junctionTarget 'react.js')) '... without touching what its junction points at' $run.Output
    Check ((Test-Path $orphanBusy) -and $run.Output -match 'kept: in use by another process') 'keeps an orphan folder another process is in, without failing the run' $run.Output
    Check ((Test-Path (Join-Path $orphanFiles 'UI\left.txt')) -and $run.Output -match 'orphan folder orphan-files.*holds files') 'keeps an orphan folder holding a real file' $run.Output

    Check (-not (Test-Branch 'claude/orphan-merged')) 'deletes a merged branch no worktree has checked out' $run.Output
    Check (-not (Test-Branch 'feature/moved-on')) 'deletes a branch whose tip is a merged PR head' $run.Output
    Check (Test-Branch 'feature/orphan-unmerged') 'keeps an unmerged branch no worktree has checked out' $run.Output
    Check ((Test-Branch 'master') -and (Test-Branch 'autodev')) 'never deletes master or autodev' $run.Output
}
finally {
    if ($squatter -and -not $squatter.HasExited) { $squatter.Kill(); $squatter.WaitForExit() }
    Set-Location $toolsDir
    foreach ($link in Get-ChildItem $wtRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object { Get-Item (Join-Path $_.FullName 'UI\node_modules') -Force -ErrorAction SilentlyContinue }) {
        if ($link.LinkType -eq 'Junction') { $link.Delete() }
    }
    if (Test-Path -LiteralPath $root) {
        Get-ChildItem $root -Recurse -Force | ForEach-Object { $_.Attributes = 'Normal' }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failures) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All prune-worktrees checks passed.'
exit 0
