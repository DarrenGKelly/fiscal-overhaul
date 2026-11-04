<#
.SYNOPSIS
    Builds a test batch: one throwaway branch that merges several feature branches onto the base, so the
    combined tree is tested once instead of each branch separately.

.DESCRIPTION
    Creates (or rebuilds) feature/batch-<Name> in a worktree under .claude/worktrees/batch-<Name> (or -Path),
    reset to -Base, then merges each origin/<branch> in the order given with --no-edit. A branch that
    conflicts is left out: its merge is aborted, the conflicting files are recorded, the rest still merge,
    and the exit code is 1. Each conflict names the already-merged branch that touched the same files (or
    the base, when none did).

    The summary gives the batch sha, each included branch and its sha, the excluded ones and why, and every
    path each included branch changes, so a failing test can be traced to its branch.

    Batch branches are test-only: never PR'd, never merged. The feature branches still merge individually
    into autodev afterwards (knowledge/git-workflow.md#test-batches).

    Re-running with the same name rebuilds from scratch: a clean existing batch worktree is reset to the
    base (it is the batch's own disposable branch) and merged again. A dirty one is refused. Nothing is
    ever deleted recursively; remove a finished batch's worktree with tools/remove-worktree.ps1.

    The batch is pushed (force, since it is rebuilt from scratch) so the agents told to read
    origin/feature/batch-<Name> can; -NoPush keeps it local. -Push is accepted and changes nothing.

    -Blame reads the batch branch's merge commits (no manifest needed) and prints which included branch
    changed each given path: a failing test file, a source file, a folder, or just a file name.

    -Check then proves the combined tree in the batch worktree: tools/build.ps1 -NoDeploy (the C# mod compiles,
    the UI type-checks; nothing reaches the game's Mods folder) and tools/test.ps1 (xunit and vitest). A failure is
    a semantic conflict between the branches that merged cleanly; -Blame the files it names. Deploying the batch
    for an in-game test is the test agent's step, under the one-game rule
    (knowledge/orchestration.md#the-shared-game-instance).

.EXAMPLE
    ./tools/new-test-batch.ps1 -Name evening -Branch feature/chart-filters,fix/tax-rounding,feature/density-tiers -Check
    ./tools/new-test-batch.ps1 -Name evening -Branch feature/chart-filters,feature/density-tiers   # rebuild without the culprit
    ./tools/new-test-batch.ps1 -Name evening -Blame Tests/DensityTaxTableTests.cs,LineChart.tsx

.NOTES
    Exit 0 = every branch merged (and, with -Check, built and passed its tests); 1 = a branch was left out, -Check
    failed, or -Blame matched nothing; 2 = refused (bad arguments, dirty or foreign worktree).
    Tests: ./tools/test-new-test-batch.ps1 (throwaway repos, stub build and test scripts).
#>
param(
    [Parameter(Mandatory)] [string]$Name,
    # Branches to merge, in order: feature/x or origin/feature/x. A comma-separated string works too.
    [string[]]$Branch = @(),
    [string]$Base = 'origin/autodev',
    [string]$Path,
    # Pushing is the default; -Push stays accepted so older commands still work.
    [switch]$Push,
    [switch]$NoPush,
    # Build without deploying and run the unit tests on the combined tree.
    [switch]$Check,
    # Paths to blame against an existing batch instead of building one.
    [string[]]$Blame = @(),
    [switch]$NoFetch
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Write-Batch([string]$Message, [string]$Color = 'Gray') {
    Write-Host "new-test-batch: $Message" -ForegroundColor $Color
}

function Split-List([string[]]$Values) {
    return @($Values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-Lines([string]$At, [string[]]$Arguments) {
    $result = Invoke-Git -Path $At -Arguments $Arguments -AllowFailure
    if ($result.ExitCode -ne 0 -or -not $result.Text) { return @() }
    return @($result.Text -split "`n" | ForEach-Object { $_.TrimEnd("`r").Trim() } | Where-Object { $_ })
}

function Get-Commit([string]$At, [string]$Ref) {
    $result = Invoke-Git -Path $At -Arguments @('rev-parse', '--verify', '--quiet', "$Ref^{commit}") -AllowFailure
    if ($result.ExitCode -ne 0) { return $null }
    return $result.Text
}

function Short([string]$Sha) { if ($Sha) { return $Sha.Substring(0, [Math]::Min(10, $Sha.Length)) } return '?' }

if ($Name -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
    Write-Batch "-Name must be kebab-case (got '$Name'): it becomes feature/batch-<name>." 'Red'
    exit 2
}

$context = Get-RepoContext (Get-Location).Path
$repo = $context.CallerRoot
$batchBranch = "feature/batch-$Name"
$Branch = Split-List $Branch
$Blame = Split-List $Blame

if (-not $NoFetch) {
    Write-Batch 'Fetching...'
    $fetch = Invoke-Git -Path $repo -Arguments @('fetch', '--prune', '--quiet', 'origin') -AllowFailure
    if ($fetch.ExitCode -ne 0) { Write-Batch "fetch failed, using the refs already here: $($fetch.Error)" 'Yellow' }
}

# ---------------------------------------------------------------------------
# -Blame: which included branch changed a path
# ---------------------------------------------------------------------------

# The batch's merges, read back from its first-parent history since the base (autodev's own sync merges
# are not the batch's): each merge commit's second parent is the branch that was merged, and its message
# names it. The base only moves forward, so a batch built on an older one still reads correctly.
function Get-BatchMerges([string]$BatchRef) {
    $merges = @()
    $range = if (Get-Commit $repo $Base) { "$Base..$BatchRef" } else { $BatchRef }
    foreach ($line in (Get-Lines $repo @('log', '--first-parent', '--merges', '--reverse', '--format=%H %P%x09%s', $range))) {
        $head, $subject = $line -split "`t", 2
        $shas = @($head -split ' ' | Where-Object { $_ })
        if ($shas.Count -lt 3) { continue }
        $branchName = if ($subject -match "branch '(?:origin/)?([^']+)'") { $Matches[1] } else { Short $shas[2] }
        $merges += [pscustomobject]@{
            Branch = $branchName
            Sha    = $shas[2]
            Paths  = @(Get-Lines $repo @('diff', '--name-only', "$($shas[1])...$($shas[2])"))
        }
    }
    return $merges
}

function Test-PathMatch([string]$Changed, [string]$Query) {
    $q = ($Query -replace '\\', '/').Trim('/')
    if (-not $q) { return $false }
    return $Changed -eq $q -or $Changed.StartsWith("$q/") -or $Changed.EndsWith("/$q")
}

if ($Blame.Count -gt 0) {
    $batchRef = if (Get-Commit $repo "refs/heads/$batchBranch") { $batchBranch } elseif (Get-Commit $repo "refs/remotes/origin/$batchBranch") { "origin/$batchBranch" } else { $null }
    if (-not $batchRef) {
        Write-Batch "No batch branch $batchBranch here or on origin." 'Red'
        exit 2
    }

    $merges = Get-BatchMerges $batchRef
    Write-Host "Blame against $batchRef ($(Short (Get-Commit $repo $batchRef))), $($merges.Count) merged branch(es):"
    $unmatched = 0
    foreach ($query in $Blame) {
        $hits = @()
        foreach ($merge in $merges) {
            $files = @($merge.Paths | Where-Object { Test-PathMatch $_ $query })
            if ($files.Count) { $hits += "  $($merge.Branch) ($(Short $merge.Sha)): $($files -join ', ')" }
        }
        Write-Host "$query"
        if ($hits.Count) { $hits | ForEach-Object { Write-Host $_ } }
        else {
            Write-Host '  no included branch changed it (it comes from the base)'
            $unmatched++
        }
    }
    exit ([int]($unmatched -eq $Blame.Count))
}

# ---------------------------------------------------------------------------
# Build: resolve what goes in
# ---------------------------------------------------------------------------

if ($Branch.Count -eq 0) {
    Write-Batch 'Give -Branch (the branches to combine) or -Blame (paths to trace).' 'Red'
    exit 2
}

$baseSha = Get-Commit $repo $Base
if (-not $baseSha) {
    Write-Batch "Base $Base does not exist here." 'Red'
    exit 2
}

$worktree = if ($Path) { if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path (Get-Location).Path $Path } } else { Join-Path $context.MainCheckout ".claude/worktrees/batch-$Name" }
$worktree = ConvertTo-NormalizedPath $worktree

# Which worktree has each branch checked out.
$checkedOut = @{}
$current = $null
foreach ($line in (Get-Lines $repo @('worktree', 'list', '--porcelain'))) {
    if ($line -like 'worktree *') { $current = ConvertTo-NormalizedPath $line.Substring(9).Trim() }
    elseif ($line -like 'branch *' -and $current) { $checkedOut[($line.Substring(7).Trim() -replace '^refs/heads/', '')] = $current }
}
$registered = @(Get-Lines $repo @('worktree', 'list', '--porcelain') | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9).Trim() })
$isRegistered = [bool]($registered | Where-Object { Test-SamePath $_ $worktree })

# ---------------------------------------------------------------------------
# The worktree: reuse a clean one, else create it
# ---------------------------------------------------------------------------

if ($isRegistered) {
    $onBranch = (Invoke-Git -Path $worktree -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure).Text
    if ($onBranch -ne $batchBranch) {
        Write-Batch "$worktree is a worktree on '$onBranch', not $batchBranch. Refusing to reset it." 'Red'
        exit 2
    }
    $gitDir = (Invoke-Git -Path $worktree -Arguments @('rev-parse', '--absolute-git-dir')).Text
    if (Test-Path -LiteralPath (Join-Path $gitDir 'MERGE_HEAD')) {
        # A merge a previous build left half-done (it was interrupted): the batch's own state, safe to drop.
        Invoke-Git -Path $worktree -Arguments @('merge', '--abort') -AllowFailure | Out-Null
    }
    $dirty = @(Get-Lines $worktree @('status', '--porcelain'))
    if ($dirty.Count) {
        Write-Batch "$worktree has uncommitted changes; a rebuild would lose them. Commit them on their own branch, or remove the batch with tools/remove-worktree.ps1:" 'Red'
        $dirty | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
        exit 2
    }
    Write-Batch "Rebuilding $batchBranch in $worktree from $Base ($(Short $baseSha))."
    Invoke-Git -Path $worktree -Arguments @('reset', '--quiet', '--hard', $baseSha) | Out-Null
}
else {
    if (Test-Path -LiteralPath $worktree) {
        Write-Batch "$worktree exists but is not a git worktree. Pick another -Path or -Name." 'Red'
        exit 2
    }
    if ($checkedOut.ContainsKey($batchBranch)) {
        Write-Batch "$batchBranch is checked out in $($checkedOut[$batchBranch]); rebuild it there (-Path) or remove that worktree with tools/remove-worktree.ps1." 'Red'
        exit 2
    }
    Write-Batch "Creating $batchBranch in $worktree from $Base ($(Short $baseSha))."
    # -B: a leftover local batch branch from an earlier build is reset, not reused.
    Invoke-Git -Path $repo -Arguments @('worktree', 'add', '--quiet', '-B', $batchBranch, $worktree, $baseSha) | Out-Null
}

# ---------------------------------------------------------------------------
# Merge each branch in order
# ---------------------------------------------------------------------------

$included = @()
$excluded = @()

foreach ($entry in $Branch) {
    $short = $entry -replace '^origin/', ''
    $ref = "origin/$short"
    $sha = Get-Commit $repo $ref
    if (-not $sha) {
        $excluded += [pscustomobject]@{ Branch = $short; Sha = $null; Reason = "not found on origin (push it?)" }
        continue
    }
    $paths = @(Get-Lines $repo @('diff', '--name-only', "$baseSha...$sha"))

    $merge = Invoke-Git -Path $worktree -Arguments @('merge', '--no-edit', '--no-ff', '--quiet', $ref) -AllowFailure
    if ($merge.ExitCode -eq 0) {
        $included += [pscustomobject]@{ Branch = $short; Sha = $sha; Paths = $paths }
        continue
    }

    $conflicts = @(Get-Lines $worktree @('diff', '--name-only', '--diff-filter=U'))
    Invoke-Git -Path $worktree -Arguments @('merge', '--abort') -AllowFailure | Out-Null
    if (-not $conflicts.Count) {
        $excluded += [pscustomobject]@{ Branch = $short; Sha = $sha; Reason = "merge failed: $(($merge.Error + ' ' + $merge.Text).Trim())" }
        continue
    }

    # Blame the conflict on the already-merged branches that changed the same files, else on the base.
    $with = @()
    foreach ($other in $included) {
        $shared = @($conflicts | Where-Object { $other.Paths -contains $_ })
        if ($shared.Count) { $with += "$($other.Branch) ($($shared -join ', '))" }
    }
    $against = if ($with.Count) { "conflicts with $($with -join '; ')" } else { "conflicts with the base $Base ($($conflicts -join ', '))" }
    $excluded += [pscustomobject]@{ Branch = $short; Sha = $sha; Reason = $against; Conflicts = $conflicts }
}

$batchSha = Get-Commit $worktree 'HEAD'

# ---------------------------------------------------------------------------
# Optional check (build without deploying, unit tests), push
# ---------------------------------------------------------------------------

# The batch's own tools/build.ps1 and tools/test.ps1, so the check is the one the merged branches define.
$checkFailed = $false
if ($Check) {
    $shell = (Get-Process -Id $PID).Path
    foreach ($step in @(@('build.ps1', '-NoDeploy'), @('test.ps1'))) {
        $script = Join-Path $worktree "tools/$($step[0])"
        if (-not (Test-Path -LiteralPath $script)) {
            Write-Batch "No tools/$($step[0]) in the batch; -Check can't run it." 'Red'
            $checkFailed = $true
            continue
        }
        Write-Batch "Check: tools/$($step -join ' ')..."
        $global:LASTEXITCODE = 0
        Push-Location -LiteralPath $worktree
        $ErrorActionPreference = 'Continue'   # 5.1 would throw on the child's redirected stderr under Stop
        try { & $shell -NoProfile -ExecutionPolicy Bypass -File $script @($step | Select-Object -Skip 1) 2>&1 | ForEach-Object { Write-Host $_ } }
        finally { Pop-Location; $ErrorActionPreference = 'Stop' }
        if ($LASTEXITCODE -ne 0) {
            $checkFailed = $true
            Write-Batch "tools/$($step[0]) failed on the combined tree (exit $LASTEXITCODE): a semantic conflict between branches that merged cleanly. See the errors above; -Blame the files they name." 'Red'
        }
    }
}

$pushed = $false
$shouldPush = -not $NoPush
if ($shouldPush) {
    # The batch is rebuilt from scratch each time, so its remote copy is always replaced.
    # Not $push: PowerShell variables are case-insensitive, and that one is the [switch]$Push parameter.
    $pushResult = Invoke-Git -Path $worktree -Arguments @('push', '--quiet', '--force', '-u', 'origin', "HEAD:refs/heads/$batchBranch") -AllowFailure
    $pushed = $pushResult.ExitCode -eq 0
    if (-not $pushed) { Write-Batch "push failed: $($pushResult.Error)" 'Red' }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ''
Write-Host "Batch $batchBranch at $batchSha$(if ($pushed) { ' (pushed)' } elseif ($shouldPush) { ' (NOT pushed)' } else { ' (local only: -NoPush)' })"
Write-Host "  base:     $Base $(Short $baseSha)"
Write-Host "  worktree: $worktree"
Write-Host "Included ($($included.Count)), merged in this order:"
if (-not $included.Count) { Write-Host '  (none)' }
foreach ($row in $included) { Write-Host "  $($row.Branch)  $(Short $row.Sha)  ($($row.Paths.Count) paths)" }
if ($excluded.Count) {
    Write-Host "Excluded ($($excluded.Count)):" -ForegroundColor Red
    foreach ($row in $excluded) { Write-Host "  $($row.Branch)  $(Short $row.Sha)  $($row.Reason)" -ForegroundColor Red }
}
if ($Check) { Write-Host "Check (build -NoDeploy, unit tests): $(if ($checkFailed) { 'FAILED' } else { 'passed' })" }
Write-Host 'Changed paths per included branch (blame a failing file with -Blame <path>):'
foreach ($row in $included) {
    Write-Host "  $($row.Branch):"
    $row.Paths | ForEach-Object { Write-Host "    $_" }
}

if ($excluded.Count -or $checkFailed -or ($shouldPush -and -not $pushed)) { exit 1 }
exit 0
