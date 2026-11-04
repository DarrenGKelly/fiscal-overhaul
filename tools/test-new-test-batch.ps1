<#
.SYNOPSIS
    Tests tools/new-test-batch.ps1 against a throwaway repo.

.DESCRIPTION
    Never touches this repo: it builds a repo with a bare origin under TEMP, feature branches that merge
    cleanly, one that conflicts with another branch and one that conflicts with the base, and deletes it all
    afterwards (the folder holds no links, and cleanup refuses if it finds one). Each pass case has its fail
    case: a clean batch vs a conflicting branch left out, a clean rebuild vs a dirty worktree refused, a
    path blamed on its branch vs a path no branch changed. Run after changing the script:
        ./tools/test-new-test-batch.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$batchScript = Join-Path $toolsDir 'new-test-batch.ps1'
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

function Invoke-Batch([hashtable]$Arguments) {
    $global:LASTEXITCODE = 0
    Push-Location $repo
    try { $output = & $batchScript @Arguments *>&1 | Out-String }
    finally { Pop-Location }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-batch-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$origin = Join-Path $root 'origin.git'
$repo = Join-Path $root 'repo'
$batchWorktree = Join-Path $repo '.claude/worktrees/batch-evening'

# A branch cut from master with one commit writing each file, pushed to origin.
function New-Branch([string]$Name, [hashtable]$Files, [string]$From = 'master') {
    Git $repo @('switch', '--quiet', '-c', $Name, $From) | Out-Null
    foreach ($file in $Files.Keys) { Write-File (Join-Path $repo $file) $Files[$file] }
    Git $repo @('add', '--all') | Out-Null
    Git $repo @('commit', '--quiet', '--no-verify', '-m', $Name) | Out-Null
    Git $repo @('push', '--quiet', 'origin', $Name) | Out-Null
    Git $repo @('switch', '--quiet', 'master') | Out-Null
    return Git $repo @('rev-parse', $Name)
}

try {
    New-Item -ItemType Directory -Force $repo | Out-Null
    Git $root @('init', '--quiet', '--bare', $origin) | Out-Null
    Git $repo @('init', '--quiet', '-b', 'master') | Out-Null
    Git $repo @('config', 'user.email', 'batch@example.invalid') | Out-Null
    Git $repo @('config', 'user.name', 'batch') | Out-Null
    Git $repo @('config', 'core.hooksPath', (Join-Path $repo 'no-hooks')) | Out-Null
    Git $repo @('config', 'core.autocrlf', 'false') | Out-Null
    Write-File (Join-Path $repo '.gitignore') ".claude/`nbin/`nobj/`n"
    # Stand-in build and test scripts: build fails when the tree holds Mod/Broken.cs (a semantic conflict between
    # branches that merged cleanly), test when it holds Tests/Failing.cs. Each records that it ran, and its flags.
    $stub = @'
param([switch]$NoDeploy)
$root = Split-Path -Parent $PSScriptRoot
$name = Split-Path -Leaf $PSCommandPath
Write-Host "stub-$name ran$(if ($NoDeploy) { ' -NoDeploy' })"
if ($name -eq 'build.ps1' -and (Test-Path (Join-Path $root 'Mod/Broken.cs'))) { Write-Host 'Mod/Broken.cs(1,1): error CS0103'; exit 1 }
if ($name -eq 'test.ps1' -and (Test-Path (Join-Path $root 'Tests/Failing.cs'))) { Write-Host 'Failed Tests.Failing'; exit 1 }
exit 0
'@
    Write-File (Join-Path $repo 'tools/build.ps1') $stub
    Write-File (Join-Path $repo 'tools/test.ps1') $stub
    Write-File (Join-Path $repo 'readme.txt') "base`n"
    Write-File (Join-Path $repo 'Mod/shared.txt') "one`ntwo`nthree`n"
    Git $repo @('add', '--all') | Out-Null
    Git $repo @('commit', '--quiet', '--no-verify', '-m', 'base') | Out-Null
    Git $repo @('remote', 'add', 'origin', $origin) | Out-Null
    Git $repo @('push', '--quiet', 'origin', 'master', 'master:autodev') | Out-Null

    $shaA = New-Branch 'feature/a' @{ 'Mod/A/a.cs' = "a`n" }
    $shaB = New-Branch 'feature/b' @{ 'Mod/B/b.cs' = "b`n"; 'Mod/shared.txt' = "ONE-b`ntwo`nthree`n" }
    $shaC = New-Branch 'fix/c' @{ 'Tests/C/CTests.cs' = "c`n" }
    New-Branch 'feature/d' @{ 'Mod/D/d.cs' = "d`n"; 'Mod/shared.txt' = "ONE-d`ntwo`nthree`n" } | Out-Null
    New-Branch 'feature/e' @{ 'readme.txt' = "base, e`n" } | Out-Null

    # autodev moves on past every branch point, changing readme.txt: feature/e now conflicts with the base.
    # It also carries a merge commit of its own (like an autodev sync), which blame must not read as the batch's.
    New-Branch 'sync' @{ 'Mod/sync.txt' = "sync`n" } | Out-Null
    Git $repo @('switch', '--quiet', '-c', 'autodev-tip', 'master') | Out-Null
    Git $repo @('merge', '--quiet', '--no-ff', '--no-edit', 'sync') | Out-Null
    Write-File (Join-Path $repo 'readme.txt') "base, autodev`n"
    Git $repo @('commit', '--quiet', '--no-verify', '-am', 'autodev moves') | Out-Null
    Git $repo @('push', '--quiet', 'origin', 'HEAD:autodev') | Out-Null
    Git $repo @('switch', '--quiet', 'master') | Out-Null
    $autodevSha = Git $repo @('rev-parse', 'autodev-tip')

    # -----------------------------------------------------------------------
    # Pass: three branches merge cleanly
    # -----------------------------------------------------------------------
    $clean = Invoke-Batch @{ Name = 'evening'; Branch = @('feature/a', 'origin/feature/b', 'fix/c') }
    Check ($clean.ExitCode -eq 0) 'a clean batch of three branches exits 0' $clean.Output
    Check ((Git $batchWorktree @('symbolic-ref', '--short', 'HEAD')) -eq 'feature/batch-evening') '... in a worktree on feature/batch-evening' $clean.Output
    $merges = @((Git $repo @('log', '--first-parent', '--merges', '--format=%P', "$autodevSha..feature/batch-evening")) -split "`n" | Where-Object { $_ })
    Check ($merges.Count -eq 3) '... with one merge commit per branch, on top of origin/autodev' $clean.Output
    Check ((Test-Path (Join-Path $batchWorktree 'Mod/A/a.cs')) -and (Test-Path (Join-Path $batchWorktree 'Mod/B/b.cs')) -and (Test-Path (Join-Path $batchWorktree 'Tests/C/CTests.cs'))) '... holding every branch''s files' $clean.Output
    Check ($clean.Output -match "feature/a\s+$($shaA.Substring(0, 10))" -and $clean.Output -match "fix/c\s+$($shaC.Substring(0, 10))") 'the summary lists each included branch with its sha' $clean.Output
    Check ($clean.Output -match '(?s)feature/b:\s+Mod/B/b\.cs\s+Mod/shared\.txt') '... and the paths each branch changed' $clean.Output
    Check ($clean.Output -notmatch 'Excluded') '... and nothing excluded' $clean.Output
    Check ((Git $repo @('rev-parse', 'origin/feature/batch-evening')) -eq (Git $batchWorktree @('rev-parse', 'HEAD'))) '... and pushed without being asked (no -Push)' $clean.Output

    # -----------------------------------------------------------------------
    # Fail: a branch conflicting with an included one, one with the base, one that doesn't exist
    # -----------------------------------------------------------------------
    $conflict = Invoke-Batch @{ Name = 'evening'; Branch = @('feature/a,feature/b,feature/d,feature/e,feature/missing,fix/c'); Push = $true }
    Check ($conflict.ExitCode -eq 1) 'a batch with a conflicting branch exits 1' $conflict.Output
    Check ($conflict.Output -match 'feature/d\s+\S+\s+conflicts with feature/b \(Mod/shared\.txt\)') '... names the already-merged branch it conflicts with, and the file' $conflict.Output
    Check ($conflict.Output -notmatch 'feature/d\s+\S+\s+conflicts with feature/a') '... and not a merged branch that touched other files' $conflict.Output
    Check ($conflict.Output -match 'feature/e\s+\S+\s+conflicts with the base origin/autodev \(readme\.txt\)') 'a conflict no included branch caused is blamed on the base' $conflict.Output
    Check ($conflict.Output -match 'feature/missing.*not found on origin') 'a branch not on origin is excluded and says so' $conflict.Output
    Check ((Test-Path (Join-Path $batchWorktree 'Tests/C/CTests.cs')) -and -not (Test-Path (Join-Path $batchWorktree 'Mod/D/d.cs'))) 'the branches after a conflict still merge; the conflicting one is left out' $conflict.Output
    $gitDir = Git $batchWorktree @('rev-parse', '--absolute-git-dir')
    Check (-not (Test-Path (Join-Path $gitDir 'MERGE_HEAD')) -and -not (Git $batchWorktree @('status', '--porcelain'))) 'the aborted merges leave the worktree clean' $conflict.Output
    Check ((Git $repo @('rev-parse', 'origin/feature/batch-evening')) -eq (Git $batchWorktree @('rev-parse', 'HEAD'))) '-Push pushes the batch branch' $conflict.Output

    # -----------------------------------------------------------------------
    # Blame: a path maps to its branch; a path no branch changed doesn't
    # -----------------------------------------------------------------------
    $blame = Invoke-Batch @{ Name = 'evening'; Blame = @('Tests/C/CTests.cs', 'shared.txt', 'Mod\B') }
    Check ($blame.ExitCode -eq 0) '-Blame exits 0 when it matches' $blame.Output
    Check ($blame.Output -match '(?m)^Tests/C/CTests\.cs\s*\r?\n\s+fix/c ') '... maps a test file to the branch that added it' $blame.Output
    Check ($blame.Output -match '(?m)^shared\.txt\s*\r?\n\s+feature/b \(\w+\): Mod/shared\.txt') '... maps a bare file name to its branch' $blame.Output
    Check ($blame.Output -match '(?m)^Mod\\B\s*\r?\n\s+feature/b ') '... maps a folder (either slash) to its branch' $blame.Output
    Check ($blame.Output -notmatch 'feature/d') '... never names an excluded branch' $blame.Output
    $unblamed = Invoke-Batch @{ Name = 'evening'; Blame = @('readme.txt', 'Mod/sync.txt') }
    Check ($unblamed.ExitCode -eq 1 -and $unblamed.Output -match 'no included branch changed it') 'a path no included branch changed is reported as the base''s, exit 1' $unblamed.Output
    Check ($unblamed.Output -notmatch '\s+sync \(') '... including one a merge inside the base brought in' $unblamed.Output

    # -----------------------------------------------------------------------
    # Rebuild with the same name: from scratch, dropping a branch
    # -----------------------------------------------------------------------
    $rebuild = Invoke-Batch @{ Name = 'evening'; Branch = @('feature/a', 'fix/c'); Push = $true }
    Check ($rebuild.ExitCode -eq 0 -and $rebuild.Output -match 'Rebuilding') 'rebuilding with the same name reuses the clean worktree and exits 0' $rebuild.Output
    Check (-not (Test-Path (Join-Path $batchWorktree 'Mod/B/b.cs')) -and (Test-Path (Join-Path $batchWorktree 'Mod/A/a.cs'))) '... starting from the base: a dropped branch is gone' $rebuild.Output
    $rebuiltMerges = @((Git $repo @('log', '--first-parent', '--merges', '--format=%H', "$autodevSha..feature/batch-evening")) -split "`n" | Where-Object { $_ })
    Check ($rebuiltMerges.Count -eq 2) '... with only the new merges' $rebuild.Output
    Check ((Git $repo @('rev-parse', 'origin/feature/batch-evening')) -eq (Git $batchWorktree @('rev-parse', 'HEAD'))) '... and -Push replaces the remote batch' $rebuild.Output
    $remoteBefore = Git $repo @('rev-parse', 'origin/feature/batch-evening')
    $local = Invoke-Batch @{ Name = 'evening'; Branch = @('feature/a'); NoPush = $true }
    Check ($local.ExitCode -eq 0 -and $local.Output -match 'local only') '-NoPush builds the batch and says it stayed local' $local.Output
    Check ((Git $repo @('rev-parse', 'origin/feature/batch-evening')) -eq $remoteBefore -and (Git $batchWorktree @('rev-parse', 'HEAD')) -ne $remoteBefore) '... leaving the remote batch where it was' $local.Output
    $rebuild = Invoke-Batch @{ Name = 'evening'; Branch = @('feature/a', 'fix/c') }
    $reblame = Invoke-Batch @{ Name = 'evening'; Blame = @('Mod/B/b.cs') }
    Check ($reblame.ExitCode -eq 1) 'after the rebuild, blame no longer finds the dropped branch' $reblame.Output

    Write-File (Join-Path $batchWorktree 'Mod/A/wip.cs') "unsaved`n"
    $dirty = Invoke-Batch @{ Name = 'evening'; Branch = @('feature/a') }
    Check ($dirty.ExitCode -eq 2 -and $dirty.Output -match 'uncommitted') 'a dirty batch worktree is refused (exit 2)' $dirty.Output
    Check (Test-Path (Join-Path $batchWorktree 'Mod/A/wip.cs')) '... and its changes are kept' $dirty.Output

    $badName = Invoke-Batch @{ Name = 'Bad_Name'; Branch = @('feature/a') }
    Check ($badName.ExitCode -eq 2) 'a non-kebab-case name is refused' $badName.Output

    # -----------------------------------------------------------------------
    # -Check: build without deploying, then the unit tests, on the combined tree
    # -----------------------------------------------------------------------
    $checked = Invoke-Batch @{ Name = 'checked'; Branch = @('feature/a'); Check = $true; NoPush = $true }
    Check ($checked.ExitCode -eq 0 -and $checked.Output -match 'Check \(build -NoDeploy, unit tests\): passed') 'a batch that builds and passes its tests passes -Check' $checked.Output
    Check ($checked.Output -match '(?s)stub-build\.ps1 ran -NoDeploy.*stub-test\.ps1 ran') '... building with -NoDeploy (nothing reaches the game), then testing' $checked.Output
    New-Branch 'feature/broken' @{ 'Mod/Broken.cs' = "broken`n" } | Out-Null
    $broken = Invoke-Batch @{ Name = 'checked'; Branch = @('feature/a', 'feature/broken'); Check = $true; NoPush = $true }
    Check ($broken.ExitCode -eq 1 -and $broken.Output -match 'build\.ps1 failed on the combined tree' -and $broken.Output -match 'semantic conflict') 'fail case: a combined tree that does not build fails -Check (exit 1), named as a semantic conflict' $broken.Output
    New-Branch 'feature/failing' @{ 'Tests/Failing.cs' = "fails`n" } | Out-Null
    $failing = Invoke-Batch @{ Name = 'checked'; Branch = @('feature/failing'); Check = $true; NoPush = $true }
    Check ($failing.ExitCode -eq 1 -and $failing.Output -match 'test\.ps1 failed on the combined tree') 'fail case: a failing unit test fails -Check' $failing.Output
    $unchecked = Invoke-Batch @{ Name = 'checked'; Branch = @('feature/failing'); NoPush = $true }
    Check ($unchecked.ExitCode -eq 0 -and $unchecked.Output -notmatch 'stub-') 'control: without -Check nothing is built or tested' $unchecked.Output
}
finally {
    Set-Location $toolsDir
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All new-test-batch checks passed.'
exit 0
