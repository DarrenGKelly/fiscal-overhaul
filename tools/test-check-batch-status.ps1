<#
.SYNOPSIS
    Tests tools/check-batch-status.ps1 against a throwaway repo with several branches and worktrees.

.DESCRIPTION
    Never touches this repo, the game or its Mods folder: the sweep is read-only, and everything it reads is
    built under TEMP and deleted afterwards. The fail cases are the point - that a branch which is NOT behind is
    not reported as behind, that a clean worktree is not reported dirty, and that a Mods folder without a
    build-info.txt reports the deployed mod as unknown. Run after changing check-batch-status.ps1:
        ./tools/test-check-batch-status.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$statusScript = Join-Path $toolsDir 'check-batch-status.ps1'
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

function Commit-File([string]$Worktree, [string]$Name, [string]$Text) {
    Write-File (Join-Path $Worktree $Name) $Text
    Git $Worktree @('add', '--', $Name) | Out-Null
    Git $Worktree @('commit', '--quiet', '--no-verify', '-m', "change $Name") | Out-Null
}

function Invoke-Status([string]$From, [hashtable]$Arguments) {
    Push-Location -LiteralPath $From
    try {
        $global:LASTEXITCODE = 0
        $output = & $statusScript @Arguments *>&1 | Out-String
        $code = $LASTEXITCODE
    }
    catch {
        $output = $_ | Out-String
        $code = 1
    }
    finally { Pop-Location }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

# Grabs the reported block for one branch: its line plus the indented notes under it
function Get-BranchBlock([string]$Output, [string]$Name) {
    $lines = $Output -split "`n"
    $block = @()
    $collecting = $false
    foreach ($line in $lines) {
        if ($line -match "^\s{2}$([regex]::Escape($Name))\s") { $collecting = $true; $block += $line; continue }
        if ($collecting) {
            if ($line -match '^\s{6}\S') { $block += $line } else { break }
        }
    }
    return ($block -join "`n")
}

# ---------------------------------------------------------------------------
# Throwaway repo: base + four branches in four states
# ---------------------------------------------------------------------------

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-batch-status-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$main = Join-Path $root 'main'
$wtAhead = Join-Path $root 'wt-ahead'
$wtBehind = Join-Path $root 'wt-behind'
$wtDirty = Join-Path $root 'wt-dirty'
New-Item -ItemType Directory -Force $main | Out-Null

try {
    Git $main @('init', '--quiet', '-b', 'master') | Out-Null
    Git $main @('config', 'user.email', 'batch@example.invalid') | Out-Null
    Git $main @('config', 'user.name', 'batch') | Out-Null
    Git $main @('config', 'core.hooksPath', (Join-Path $root 'no-hooks')) | Out-Null
    Git $main @('config', 'core.autocrlf', 'false') | Out-Null

    Write-File (Join-Path $main 'readme.txt') 'base'
    Git $main @('add', '--all') | Out-Null
    Git $main @('commit', '--quiet', '--no-verify', '-m', 'initial') | Out-Null
    $baseCommit = Git $main @('rev-parse', 'HEAD')
    Git $main @('update-ref', 'refs/remotes/origin/autodev', $baseCommit) | Out-Null

    # feature/behind: forked from the FIRST base, before the base moves on below. Not pushed at all.
    Git $main @('worktree', 'add', '--quiet', $wtBehind, '-b', 'feature/behind', $baseCommit) | Out-Null
    Commit-File $wtBehind 'behind.txt' 'b'

    # the base moves on, so feature/behind (and only feature/behind) falls behind it
    Commit-File $main 'moved.txt' 'the base moved on'
    Git $main @('update-ref', 'refs/remotes/origin/autodev', (Git $main @('rev-parse', 'HEAD'))) | Out-Null

    # feature/ahead: one commit past the CURRENT base, pushed, clean
    Git $main @('worktree', 'add', '--quiet', $wtAhead, '-b', 'feature/ahead') | Out-Null
    Commit-File $wtAhead 'ahead.txt' 'a'
    Git $main @('update-ref', 'refs/remotes/origin/feature/ahead', (Git $wtAhead @('rev-parse', 'HEAD'))) | Out-Null

    # feature/dirty: also current and pushed, but with uncommitted work in its worktree
    Git $main @('worktree', 'add', '--quiet', $wtDirty, '-b', 'feature/dirty') | Out-Null
    Commit-File $wtDirty 'dirty.txt' 'd'
    Git $main @('update-ref', 'refs/remotes/origin/feature/dirty', (Git $wtDirty @('rev-parse', 'HEAD'))) | Out-Null
    Write-File (Join-Path $wtDirty 'dirty.txt') 'd edited but not committed'

    # a fix/ branch, to prove -Pattern filters
    Git $main @('branch', 'fix/unrelated', $baseCommit) | Out-Null

    $mods = Join-Path $root 'Mods'

    # --- the sweep itself ---
    $r = Invoke-Status $wtAhead @{ ModsPath = $mods }
    Check ($r.ExitCode -eq 0) 'the sweep exits 0' $r.Output

    $aheadBlock = Get-BranchBlock $r.Output 'feature/ahead'
    $behindBlock = Get-BranchBlock $r.Output 'feature/behind'
    $dirtyBlock = Get-BranchBlock $r.Output 'feature/dirty'

    Check ($aheadBlock -match 'ahead 1, behind 0') 'an up-to-date branch reports its ahead count' $aheadBlock
    Check ($behindBlock -match 'behind 1' -and $behindBlock -cmatch 'BEHIND the base') 'a branch behind the base is called out' $behindBlock

    # fail cases: the notes must NOT appear where they do not apply
    # -cnotmatch: 'behind 0' on the branch's own line would match a case-insensitive 'BEHIND'
    Check ($aheadBlock -cnotmatch 'BEHIND') 'a branch that is not behind is NOT reported as behind' $aheadBlock
    Check ($aheadBlock -notmatch 'uncommitted') 'a clean worktree is NOT reported dirty' $aheadBlock
    Check ($aheadBlock -notmatch 'not pushed') 'a pushed branch is NOT reported unpushed' $aheadBlock

    Check ($behindBlock -match 'not pushed') 'an unpushed branch is reported unpushed' $behindBlock
    Check ($dirtyBlock -match 'uncommitted file') 'a worktree with uncommitted work is called out' $dirtyBlock
    Check ($dirtyBlock -notmatch 'not pushed') 'the dirty branch is still recognised as pushed' $dirtyBlock

    Check ($r.Output -notmatch 'fix/unrelated') "-Pattern 'feature/*' leaves fix/ branches out" $r.Output

    $r = Invoke-Status $wtAhead @{ Pattern = 'fix/*'; ModsPath = $mods }
    Check ($r.Output -match 'fix/unrelated' -and $r.Output -notmatch 'feature/ahead') '-Pattern selects a different set' $r.Output

    $r = Invoke-Status $wtAhead @{ Branch = @('feature/dirty') }
    Check ($r.Output -match 'feature/dirty' -and $r.Output -notmatch 'feature/ahead') 'an explicit -Branch list overrides the pattern' $r.Output

    $r = Invoke-Status $wtAhead @{ Branch = @('feature/nope') }
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'does not exist locally') 'a missing branch is reported, not fatal' $r.Output

    # --- the shared state: the game, the deployed mod and the main checkout ---
    $r = Invoke-Status $wtAhead @{ ModsPath = $mods }
    Check ($r.Output -match 'Game: (not running|RUNNING \(Cities2\.exe pid \d+)') 'the game''s state is reported (running or not)' $r.Output
    Check ($r.Output -match 'Deployed mod: unknown') 'a Mods folder without build-info.txt reports the deployed mod as unknown' $r.Output
    Check ($r.Output -match 'Main checkout: clean') 'a clean main checkout is reported clean' $r.Output

    Write-File (Join-Path $mods 'FiscalOverhaul\build-info.txt') "branch feature/ahead`ncommit 0123456789`n"
    $r = Invoke-Status $wtAhead @{ ModsPath = $mods }
    Check ($r.Output -match 'Deployed mod: branch feature/ahead, commit 0123456789') 'a build-info.txt is reported as the deployed mod' $r.Output
    Check ($r.Output -notmatch 'Deployed mod: unknown') '... and not as unknown' $r.Output

    Write-File (Join-Path $main 'readme.txt') 'someone is editing'
    $r = Invoke-Status $wtAhead @{ ModsPath = $mods }
    Check ($r.Output -match 'Main checkout: 1 uncommitted') 'a dirty main checkout is called out' $r.Output
    Git $main @('checkout', '--', 'readme.txt') | Out-Null

    # --- -PassThru gives a caller the rows ---
    Push-Location -LiteralPath $wtAhead
    try { $rows = & $statusScript -PassThru -ModsPath $mods 6>$null }
    finally { Pop-Location }
    Check ($rows.Count -eq 3) '-PassThru emits one object per branch' ($rows | Out-String)
    $aheadRow = $rows | Where-Object { $_.Branch -eq 'feature/ahead' }
    Check ($aheadRow.Ahead -eq 1 -and $aheadRow.Behind -eq 0 -and $aheadRow.Push -eq 'pushed' -and $aheadRow.DirtyFiles -eq 0) '-PassThru rows carry the real numbers' ($aheadRow | Out-String)
}
finally {
    if (Test-Path $root) {
        Get-ChildItem $root -Recurse -Force | ForEach-Object { $_.Attributes = 'Normal' }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failures -eq 0) {
    Write-Host 'All check-batch-status tests passed.'
    exit 0
}

Write-Host "$failures check-batch-status test(s) failed."
exit 1
