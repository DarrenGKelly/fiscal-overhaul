<#
.SYNOPSIS
    Removes finished worktrees under .claude/worktrees/ and deletes merged local branches.

.DESCRIPTION
    A session can't remove its own worktree (Windows refuses to delete the folder it runs in) or delete
    the branch that worktree has checked out, so finished ones pile up. This sweeps them from outside:
    run it at the start of a session, or from the main checkout.

    A worktree is removed only when ALL of these hold:
      - it lives under <main checkout>/.claude/worktrees/ (never the main checkout);
      - it isn't the one this shell is in, and isn't locked. A lock Claude Code
        took for an Agent subagent ("claude agent <id> (pid N)") is stale once process N has exited (or the
        pid now belongs to a process started after the lock), and doesn't count;
      - it has no uncommitted or untracked changes, apart from an orchestrator's leftovers (untracked tasks/
        ledgers and *captures*/ folders) once it has been idle $ArchiveMinIdleHours: those are moved to
        <main checkout>/.claude/worktree-archive/<worktree>/ first;
      - its HEAD is safe elsewhere: on a remote branch, or contained in a merged PR's head (a subagent's
        worktree usually sits a few commits behind the PR head that a fix session or a merge moved on);
      - git hasn't touched it (its HEAD, index or reflog) for -MinIdleHours. A session that just branched
        from origin/autodev passes every other check, so this is what keeps a live session's worktree.
        A test batch's worktree (feature/batch-*) always needs the default 12 hours, whatever -MinIdleHours
        says: it sits idle while its orchestrator waits on fix sessions, and a short sweep run by another
        orchestrator would take it from under that batch's test agent.
    Removal goes through tools/remove-worktree.ps1, which unlinks folder links (a linked UI/node_modules) first.

    It also clears orphan folders: a folder under .claude/worktrees/ that git no longer lists as a worktree
    (Claude Code's own cleanup of a subagent's worktree, or a git removal that stopped at a link)
    and that holds no real file, only links and empty folders. Links are unlinked, never followed.
    An orphan folder with any real file in it is reported and left alone.

    A local branch is deleted only when it's feature/*, fix/*, claude/* or worktree-*, no remaining
    worktree has it checked out, and it is merged: its tip is in a merged PR's head, or already on
    origin/autodev or origin/master. A branch that is only pushed keeps its local copy.

    Dry run by default: it prints what it would do. -Apply acts.

.EXAMPLE
    ./tools/prune-worktrees.ps1                  # dry run against this repo's main checkout
    ./tools/prune-worktrees.ps1 -Apply
    ./tools/prune-worktrees.ps1 -Apply -MinIdleHours 2
#>
param(
    # Any checkout of the repo; the sweep always works from its main checkout. Defaults to this shell's.
    [string]$Path = '',

    [switch]$Apply,

    # A worktree git touched more recently than this is treated as a live session's and kept.
    # Batch worktrees (feature/batch-*) never go below $BatchMinIdleHours.
    [double]$MinIdleHours = 12,

    # Merged PR head commits. Defaults to asking gh; tests pass their own.
    [string[]]$MergedHead = $null,

    # Don't ask gh: only commits already on a remote branch (or on origin/autodev/master) count as landed.
    [switch]$NoGitHub
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

if (-not $Path) { $Path = (Get-Location).Path }
$context = Get-RepoContext $Path
$main = $context.MainCheckout
Write-HooksPathWarning $context.CallerRoot
$worktreesRoot = ConvertTo-NormalizedPath (Join-Path $main '.claude\worktrees')
$here = ConvertTo-NormalizedPath (Get-Location).Path
$removeScript = Join-Path $PSScriptRoot 'remove-worktree.ps1'

function Invoke-RepoGit([string[]]$Arguments, [string]$At = $main) { Invoke-Git -Path $At -Arguments $Arguments -AllowFailure }

function Test-Under([string]$Child, [string]$Parent) { return Test-PathUnder $Child $Parent }

function Get-Worktrees {
    $entries = @()
    $current = $null
    foreach ($line in ((Invoke-RepoGit @('worktree', 'list', '--porcelain')).Text -split "`r?`n")) {
        if ($line -match '^worktree (.+)$') {
            if ($current) { $entries += [pscustomobject]$current }
            $current = @{ Path = ConvertTo-NormalizedPath $Matches[1]; Head = ''; Branch = ''; Locked = $false; Prunable = $false }
        }
        elseif ($line -match '^HEAD (\w+)$') { $current.Head = $Matches[1] }
        elseif ($line -match '^branch refs/heads/(.+)$') { $current.Branch = $Matches[1] }
        elseif ($line -match '^locked') { $current.Locked = $true }
        elseif ($line -match '^prunable') { $current.Prunable = $true }
    }
    if ($current) { $entries += [pscustomobject]$current }
    return $entries
}

function Get-MergedHeads {
    if ($null -ne $MergedHead) { return @($MergedHead) }
    if ($NoGitHub) { return @() }
    $ghPath = Find-Application 'gh' @('C:\Program Files\GitHub CLI\gh.exe')
    if (-not $ghPath) {
        Write-Host 'prune-worktrees: gh not found; only commits on a remote branch count as landed.'
        return @()
    }
    Push-Location $main
    # The limit must outgrow the repo's merged PR count: past it the oldest heads drop out silently.
    try { $json = & $ghPath pr list --state merged --limit 10000 --json headRefOid 2>$null }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0 -or -not $json) {
        Write-Host 'prune-worktrees: gh pr list failed; only commits on a remote branch count as landed.'
        return @()
    }
    return @(($json | ConvertFrom-Json) | ForEach-Object { $_.headRefOid })
}

# git with lines on stdin (a thousand shas overflow a Windows command line). Returns stdout's lines.
function Invoke-GitInput([string[]]$Arguments, [string[]]$Lines) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'git'
    $startInfo.Arguments = Join-ProcessArguments (@('-C', $main) + $Arguments)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    # Windows PowerShell (.NET Framework) opens a redirected stdin in the console's input encoding and, when that is
    # UTF-8, writes its BOM at once (git then reads the first sha as a bad revision); a BOM-less UTF-8 console
    # encoding for the Start call avoids it. Without a console the setter throws, and there is no BOM to avoid.
    $savedInput = $null
    try { $savedInput = [Console]::InputEncoding; [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
    try { $process = [System.Diagnostics.Process]::Start($startInfo) }
    finally { if ($savedInput) { try { [Console]::InputEncoding = $savedInput } catch { } } }
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    try {
        foreach ($line in $Lines) { $process.StandardInput.WriteLine($line) }
        $process.StandardInput.Close()
    }
    catch { }   # git exited early; its stderr says why
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { Write-Host "prune-worktrees: git $($Arguments -join ' ') failed: $($stderrTask.Result.Trim())" }
    return @($stdoutTask.Result -split "`r?`n" | Where-Object { $_ })
}

# Every commit reachable from a merged PR head that this repo has: a worktree a few commits behind the head
# its PR merged at is as landed as one at the head. Heads never fetched here are skipped (rev-list dies on them).
function Get-LandedCommits([string[]]$Heads) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($head in $Heads) { [void]$set.Add($head) }
    if (-not $Heads) { return ,$set }
    $present = @(Invoke-GitInput @('cat-file', '--batch-check=%(objectname) %(objecttype)') $Heads |
        Where-Object { $_ -match '^\w+ commit$' } | ForEach-Object { ($_ -split ' ')[0] })
    if ($present) { foreach ($sha in Invoke-GitInput @('rev-list', '--stdin') $present) { [void]$set.Add($sha) } }
    return ,$set
}

# Claude Code locks a subagent's worktree as "claude agent <id> (pid N)" and leaves the lock when that process
# exits. Stale when no process N is running, or the one that is started after the lock was written (pid reuse).
function Test-StaleAgentLock([string]$Worktree) {
    $gitDir = (Invoke-RepoGit @('rev-parse', '--absolute-git-dir') $Worktree).Text
    $lockFile = Join-Path $gitDir 'locked'
    if (-not (Test-Path -LiteralPath $lockFile)) { return $false }
    $reason = [IO.File]::ReadAllText($lockFile)
    if ($reason -notmatch '^claude agent \S+ \(pid (\d+)\)') { return $false }
    $process = Get-Process -Id ([int]$Matches[1]) -ErrorAction SilentlyContinue
    if (-not $process) { return $true }
    try { return $process.StartTime.ToUniversalTime() -gt (Get-Item -LiteralPath $lockFile -Force).LastWriteTimeUtc }
    catch { return $false }   # can't read the start time: assume it's the owner
}

# An orchestrator keeps its ledger (tasks/<batch>/) and screenshot folders untracked in its worktree, so it
# is never clean. Returns the untracked paths exactly as status names them (tasks/<batch>/, never tasks/ when
# the branch tracks a tasks/.gitkeep) when they are the only changes, else $null.
function Get-OrchestratorLeftovers([string]$Status) {
    $paths = @()
    foreach ($line in ($Status -split "`r?`n")) {
        if (-not $line) { continue }
        if ($line -notmatch '^\?\? "?((?:tasks|[\w.-]*captures[\w.-]*)/[^"]*)"?$') { return $null }
        $paths += $Matches[1].TrimEnd('/')
    }
    if ($paths) { return $paths } else { return $null }
}

function Move-ToArchive([string]$Worktree, [string[]]$Paths) {
    $archive = Join-Path $archiveRoot (Split-Path -Leaf $Worktree)
    foreach ($path in $Paths) {
        $target = Join-Path $archive $path
        if (Test-Path -LiteralPath $target) { $target += '-' + (Get-Date -Format 'yyyyMMddHHmmss') }
        New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
        Move-Item -LiteralPath (Join-Path $Worktree $path) -Destination $target
    }
    return $archive
}

# True when the folder holds nothing but links (junctions, symlinks) and empty folders. Never follows a link.
function Test-OnlyLinks([string]$Folder) {
    foreach ($item in Get-ChildItem -LiteralPath $Folder -Force) {
        if ($item.LinkType) { continue }
        if (-not $item.PSIsContainer) { return $false }
        if (-not (Test-OnlyLinks $item.FullName)) { return $false }
    }
    return $true
}

# Unlinks every link non-recursively, then deletes the emptied folders bottom-up.
function Remove-LinkShell([string]$Folder) {
    foreach ($item in Get-ChildItem -LiteralPath $Folder -Force) {
        if ($item.LinkType) { $item.Delete() }
        elseif ($item.PSIsContainer) { Remove-LinkShell $item.FullName }
    }
    [IO.Directory]::Delete($Folder, $false)
}

function Test-Merged([string]$Sha) {
    if ($landedCommits.Contains($Sha)) { return $true }
    foreach ($target in 'origin/autodev', 'origin/master') {
        if ((Invoke-RepoGit @('rev-parse', '--verify', '--quiet', $target)).ExitCode -ne 0) { continue }
        if ((Invoke-RepoGit @('merge-base', '--is-ancestor', $Sha, $target)).ExitCode -eq 0) { return $true }
    }
    return $false
}

function Test-OnRemote([string]$Sha) {
    return [bool](Invoke-RepoGit @('branch', '-r', '--contains', $Sha)).Text
}

function Get-IdleHours([string]$Worktree) {
    $gitDir = (Invoke-RepoGit @('rev-parse', '--absolute-git-dir') $Worktree).Text
    $newest = [DateTime]::MinValue
    foreach ($name in 'HEAD', 'index', 'logs\HEAD') {
        $file = Join-Path $gitDir $name
        if (Test-Path -LiteralPath $file) {
            $time = (Get-Item -LiteralPath $file -Force).LastWriteTimeUtc
            if ($time -gt $newest) { $newest = $time }
        }
    }
    if ($newest -eq [DateTime]::MinValue) { return [double]::MaxValue }
    return ([DateTime]::UtcNow - $newest).TotalHours
}

$BatchMinIdleHours = 12
# A ledger is what an orchestrator resumes from after a crash, so its worktree waits days, not hours.
$ArchiveMinIdleHours = 72
$archiveRoot = Join-Path $main '.claude\worktree-archive'
$landedCommits = Get-LandedCommits (Get-MergedHeads)
Write-Host "prune-worktrees: $main$(if (-not $Apply) { ' (dry run: -Apply to act)' })"

$removed = 0; $kept = 0; $deleted = 0; $failed = 0; $orphans = 0
$stale = $false
$freed = @()   # branches whose worktree is (or, in a dry run, would be) removed

$worktrees = @(Get-Worktrees)
foreach ($wt in $worktrees) {
    if (Test-SamePath $wt.Path $main) { continue }
    if (-not (Test-Under $wt.Path $worktreesRoot)) { continue }
    $label = if ($wt.Branch) { $wt.Branch } else { "detached $($wt.Head.Substring(0, 8))" }
    $name = Split-Path -Leaf $wt.Path

    if ($wt.Prunable) { $stale = $true; Write-Host "  stale   $name  (folder gone: git worktree prune)"; continue }

    # Idle first, and status with --no-optional-locks: a plain 'git status' refreshes the index, which
    # would make every worktree look freshly active to this run and the next.
    $idle = Get-IdleHours $wt.Path
    $needIdle = $MinIdleHours
    if ($wt.Branch -like 'feature/batch-*') { $needIdle = [Math]::Max($MinIdleHours, $BatchMinIdleHours) }
    $reason = $null
    $staleLock = $wt.Locked -and (Test-StaleAgentLock $wt.Path)
    $leftovers = $null
    if ($wt.Locked -and -not $staleLock) { $reason = 'locked' }
    elseif (Test-Under $here $wt.Path) { $reason = 'this shell is in it' }
    elseif (($status = (Invoke-RepoGit @('--no-optional-locks', 'status', '--porcelain') $wt.Path).Text) -and
        -not ($leftovers = Get-OrchestratorLeftovers $status)) { $reason = 'uncommitted changes' }
    elseif (-not ((Test-Merged $wt.Head) -or (Test-OnRemote $wt.Head))) { $reason = 'commits not on any remote branch or merged PR' }
    elseif ($leftovers -and $idle -lt [Math]::Max($MinIdleHours, $ArchiveMinIdleHours)) {
        $reason = ('untracked {0}, git active {1:0.#}h ago (a ledger keeps {2} h)' -f ($leftovers -join ', '), $idle, $ArchiveMinIdleHours)
    }
    elseif ($idle -lt $needIdle) {
        $reason = ('git active {0:0.#}h ago' -f $idle)
        if ($needIdle -gt $MinIdleHours) { $reason += " (a test batch keeps $BatchMinIdleHours h)" }
    }

    if ($reason) { $kept++; Write-Host "  keep    $name  ($label): $reason"; continue }

    $notes = @()
    if ($staleLock) { $notes += 'stale agent lock' }
    if ($leftovers) { $notes += "archives $($leftovers -join ', ')" }
    Write-Host "  remove  $name  ($label)$(if ($notes) { ': ' + ($notes -join '; ') })"
    if (-not $Apply) { $removed++; $freed += $wt.Branch; continue }
    $global:LASTEXITCODE = 0
    $output = ''
    try {
        if ($staleLock) { Invoke-RepoGit @('worktree', 'unlock', $wt.Path) | Out-Null }
        if ($leftovers) { $output = "archived to $(Move-ToArchive $wt.Path $leftovers); " }
        Push-Location $main   # remove-worktree.ps1 runs git in the current directory
        try { $output += & $removeScript -Path $wt.Path *>&1 | Out-String }
        finally { Pop-Location }
    }
    catch { $output += $_.Exception.Message; $global:LASTEXITCODE = 1 }
    $gone = -not (Get-Worktrees | Where-Object { Test-SamePath $_.Path $wt.Path })
    if ($LASTEXITCODE -eq 0 -and $gone) { $removed++; $freed += $wt.Branch }
    else { $failed++; Write-Host "          failed: $($output.Trim() -replace '\s+', ' ')" }
}

if ($stale -and $Apply) { Invoke-RepoGit @('worktree', 'prune') | Out-Null }

# Orphan folders: under .claude/worktrees/ but no longer a worktree git knows about.
$registered = @(Get-Worktrees | ForEach-Object { $_.Path })
foreach ($folder in Get-ChildItem -LiteralPath $worktreesRoot -Directory -Force -ErrorAction SilentlyContinue) {
    if ($folder.LinkType) { continue }
    if ($registered | Where-Object { Test-SamePath $_ $folder.FullName }) { continue }
    if (Test-Under $here $folder.FullName) { continue }
    if (-not (Test-OnlyLinks $folder.FullName)) { Write-Host "  keep    orphan folder $($folder.Name): not a worktree, but holds files (check it by hand)"; continue }
    Write-Host "  clear   orphan folder $($folder.Name)  (no worktree, only links and empty folders)"
    if (-not $Apply) { $orphans++; continue }
    try { Remove-LinkShell $folder.FullName; $orphans++ }
    catch [System.IO.IOException] {
        # A session or shell still has it as its working directory; its links are gone, the next sweep retries.
        Write-Host "          kept: in use by another process (a session or shell open there)"
    }
    catch { $failed++; Write-Host "          failed: $($_.Exception.Message)" }
}

$checkedOut = @(Get-Worktrees | Where-Object { $_.Branch -and -not $_.Prunable -and $freed -notcontains $_.Branch } | ForEach-Object { $_.Branch })

foreach ($line in ((Invoke-RepoGit @('for-each-ref', '--format=%(refname:short) %(objectname)', 'refs/heads')).Text -split "`r?`n")) {
    if (-not $line) { continue }
    $branch, $sha = $line -split ' ', 2
    if ($branch -notmatch '^(feature|fix|claude)/|^worktree-') { continue }
    if ($checkedOut -contains $branch) { continue }
    if (-not (Test-Merged $sha)) { continue }
    Write-Host "  delete  branch $branch"
    if (-not $Apply) { $deleted++; continue }
    $result = Invoke-RepoGit @('branch', '-D', $branch)
    if ($result.ExitCode -eq 0) { $deleted++ } else { $failed++; Write-Host "          failed: $($result.Error)" }
}

$would = if ($Apply) { '' } else { 'would be ' }
Write-Host "prune-worktrees: $removed worktree(s) ${would}removed, $kept kept, $orphans orphan folder(s) ${would}cleared, $deleted branch(es) ${would}deleted$(if ($failed) { ", $failed failed" })."
exit $(if ($failed) { 1 } else { 0 })
