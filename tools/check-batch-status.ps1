<#
.SYNOPSIS
    One sweep over a batch of branches: how far along each is, whether the game is running, and whether the
    main checkout is clean.

.DESCRIPTION
    The orchestrator's "where is everything" question (the global /orchestrate skill), which otherwise
    gets asked by hand a dozen times an evening. Read-only: it runs no switch, no fetch (unless asked), and
    changes nothing anywhere.

    Per branch it reports commits ahead of the base, whether the branch is pushed and matches its remote,
    whether it is BEHIND the base (its test results would prove little), and whether its worktree has
    uncommitted work - which is the only work a killed session loses.

    Then the shared state: whether Cities: Skylines II is running (the one game instance every in-game test
    shares: knowledge/orchestration.md#the-shared-game-instance), what the deployed mod was built from, and
    whether the main checkout is clean.

.EXAMPLE
    ./tools/check-batch-status.ps1
    ./tools/check-batch-status.ps1 -Pattern 'feature/chart-*' -Fetch
    ./tools/check-batch-status.ps1 -Branch feature/economy-panel -Branch fix/tax-rounding

.NOTES
    Exit code is 0 unless the sweep itself could not run. It is a report, not a gate - read it and decide.
    -PassThru emits the per-branch objects so a caller can act on them. -ModsPath points at another Mods folder
    (tests); it defaults to %CSII_LOCALMODSPATH%.
    Tests: ./tools/test-check-batch-status.ps1
#>
param(
    # Branches to report on. Without either, every local branch matching -Pattern is used.
    [string[]]$Branch = @(),
    [string]$Pattern = 'feature/*',

    [string]$BaseRef = 'origin/autodev',
    [switch]$Fetch,
    [switch]$PassThru,
    [string]$ModsPath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Write-Batch([string]$Message, [string]$Color = 'Gray') {
    Write-Host "check-batch-status: $Message" -ForegroundColor $Color
}

$context = Get-RepoContext (Get-Location).Path
$repo = $context.CallerRoot
Write-HooksPathWarning $repo

if ($Fetch) {
    Write-Batch 'Fetching...'
    Invoke-Git -Path $repo -Arguments @('fetch', '--prune', '--quiet') -AllowFailure | Out-Null
}

function Get-Lines([string[]]$Arguments) {
    $result = Invoke-Git -Path $repo -Arguments $Arguments -AllowFailure
    if ($result.ExitCode -ne 0 -or -not $result.Text) { return @() }
    return @($result.Text -split "`n" | ForEach-Object { $_.TrimEnd("`r").Trim() } | Where-Object { $_ })
}

function Get-Count([string]$Range) {
    $result = Invoke-Git -Path $repo -Arguments @('rev-list', '--count', $Range) -AllowFailure
    if ($result.ExitCode -ne 0) { return $null }
    return [int]$result.Text
}

# ---------------------------------------------------------------------------
# Which branches, and which worktree holds each
# ---------------------------------------------------------------------------

if ($Branch.Count -eq 0) {
    $Branch = Get-Lines @('for-each-ref', '--format=%(refname:short)', "refs/heads/$Pattern")
}

if ($Branch.Count -eq 0) {
    Write-Batch "No local branches match '$Pattern'." 'Yellow'
}

# 'worktree list --porcelain' emits blank-line separated records of 'worktree <path>' / 'branch <ref>'
$worktrees = @{}
$currentPath = $null
foreach ($line in (Get-Lines @('worktree', 'list', '--porcelain'))) {
    if ($line -like 'worktree *') { $currentPath = $line.Substring(9).Trim() }
    elseif ($line -like 'branch *' -and $currentPath) {
        $name = $line.Substring(7).Trim() -replace '^refs/heads/', ''
        $worktrees[$name] = $currentPath
    }
}

$baseExists = $null -ne (Invoke-Git -Path $repo -Arguments @('rev-parse', '--verify', '--quiet', "$BaseRef^{commit}") -AllowFailure).Text
if (-not $baseExists) {
    Write-Batch "$BaseRef does not exist here, so ahead/behind is unknown. Run with -Fetch?" 'Yellow'
}

$rows = @()
foreach ($name in $Branch) {
    $commit = (Invoke-Git -Path $repo -Arguments @('rev-parse', '--verify', '--quiet', "$name^{commit}") -AllowFailure).Text
    if (-not $commit) {
        Write-Batch "Branch '$name' does not exist locally." 'Yellow'
        continue
    }

    $ahead = $null
    $behind = $null
    if ($baseExists) {
        $ahead = Get-Count "$BaseRef..$name"
        $behind = Get-Count "$name..$BaseRef"
    }

    $remoteCommit = (Invoke-Git -Path $repo -Arguments @('rev-parse', '--verify', '--quiet', "refs/remotes/origin/$name^{commit}") -AllowFailure).Text
    $pushState = 'not pushed'
    if ($remoteCommit -eq $commit) { $pushState = 'pushed' }
    elseif ($remoteCommit) {
        $unpushed = Get-Count "refs/remotes/origin/$name..$name"
        $pushState = "$unpushed unpushed"
    }

    $worktree = $worktrees[$name]
    $dirty = $null
    if ($worktree -and (Test-Path -LiteralPath $worktree)) {
        $dirty = (Get-Lines @('-C', $worktree, 'status', '--porcelain')).Count
    }

    $rows += [pscustomobject]@{
        Branch    = $name
        Commit    = $commit.Substring(0, 8)
        Ahead     = $ahead
        Behind    = $behind
        Push      = $pushState
        Worktree  = $worktree
        DirtyFiles = $dirty
    }
}

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------

Write-Host ''
Write-Host "Batch status (base: $BaseRef)"
if ($rows.Count -eq 0) { Write-Host '  (no branches)' }

foreach ($row in $rows) {
    $ahead = '?'
    if ($null -ne $row.Ahead) { $ahead = $row.Ahead }
    $behind = '?'
    if ($null -ne $row.Behind) { $behind = $row.Behind }

    $color = 'Gray'
    $notes = @()
    if ($row.Behind -gt 0) { $notes += 'BEHIND the base - merge before testing'; $color = 'Yellow' }
    if ($row.Push -ne 'pushed') { $notes += $row.Push; $color = 'Yellow' }
    if ($row.DirtyFiles -gt 0) { $notes += "$($row.DirtyFiles) uncommitted file(s) - a killed session loses these"; $color = 'Yellow' }
    if ($null -eq $row.Worktree) { $notes += 'no worktree' }

    Write-Host ("  {0,-42} {1}  ahead {2}, behind {3}" -f $row.Branch, $row.Commit, $ahead, $behind) -ForegroundColor $color
    foreach ($note in $notes) { Write-Host "      $note" -ForegroundColor $color }
}

if ($rows.Count -gt 12) {
    Write-Host ''
    Write-Host ("  ({0} branches matched '{1}'. Most are probably long merged - pass -Branch <name> for each" -f $rows.Count, $Pattern) -ForegroundColor DarkGray
    Write-Host '   branch in the batch, or a tighter -Pattern, to get a report you can actually read.)' -ForegroundColor DarkGray
}

Write-Host ''
$game = Get-GameProcess
if (-not $game) {
    Write-Host 'Game: not running (free for the next in-game test)'
}
else {
    $started = try { $game.StartTime.ToString('yyyy-MM-dd HH:mm') } catch { '?' }
    Write-Host "Game: RUNNING (Cities2.exe pid $($game.Id), started $started) - one in-game test at a time; don't build or launch" -ForegroundColor Yellow
}

# What the deployed mod is: tools/build.ps1 writes build-info.txt (branch, commit, time) into the deploy folder.
if (-not $ModsPath) { $ModsPath = [Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH', 'User') }
if (-not $ModsPath) { $ModsPath = $env:CSII_LOCALMODSPATH }
$info = if ($ModsPath) { Join-Path $ModsPath 'FiscalOverhaul\build-info.txt' } else { $null }
if ($info -and (Test-Path -LiteralPath $info)) {
    Write-Host "Deployed mod: $(((Get-Content -LiteralPath $info) -join ', '))"
}
else {
    Write-Host 'Deployed mod: unknown (no build-info.txt: not built by tools/build.ps1 yet)'
}

$mainDirty = (Get-Lines @('-C', $context.MainCheckout, 'status', '--porcelain')).Count
if ($mainDirty -eq 0) {
    Write-Host "Main checkout: clean ($($context.MainCheckout))" -ForegroundColor Green
}
else {
    Write-Host "Main checkout: $mainDirty uncommitted file(s) ($($context.MainCheckout))" -ForegroundColor Yellow
}

if ($PassThru) { $rows }
exit 0
