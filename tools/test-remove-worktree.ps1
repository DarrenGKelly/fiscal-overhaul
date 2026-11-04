<#
.SYNOPSIS
    Tests tools/remove-worktree.ps1 against a throwaway repo.

.DESCRIPTION
    Never touches this repo: it builds a repo under TEMP with worktrees, one whose UI/node_modules is a junction
    into a stand-in main checkout's node_modules, removes them, and deletes it all afterwards. What must hold: the
    worktree goes and the folder its link pointed at survives; gitignored build output goes with it; a dirty
    worktree is refused without -Force (its link kept) and removed with it; a path that isn't a worktree, and the
    main working tree, are refused. Every check has a pass and a fail case.
        ./tools/test-remove-worktree.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$removeScript = Join-Path $toolsDir 'remove-worktree.ps1'
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

# Runs the script in a child shell, as a session would, from inside the repo; returns exit code and output.
$shell = (Get-Process -Id $PID).Path
function Invoke-Remove([string]$From, [string[]]$Arguments) {
    Push-Location -LiteralPath $From
    # git's refusal arrives on stderr; under Stop, Windows PowerShell 5.1 would throw on it instead of returning it.
    $ErrorActionPreference = 'Continue'
    try {
        $global:LASTEXITCODE = 0
        $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $removeScript @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally { Pop-Location }
}

function New-Link([string]$Link, [string]$Target) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $Link) | Out-Null
    New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null
}

function Test-Listed([string]$Worktree) {
    $list = Git $repo @('worktree', 'list', '--porcelain')
    foreach ($line in ($list -split "`n")) {
        if ($line -match '^worktree (.+)$' -and (Test-SamePath $Matches[1].Trim() $Worktree)) { return $true }
    }
    return $false
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-remove-wt-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$repo = Join-Path $root 'repo'
$modules = Join-Path $root 'node_modules'   # stands in for the main checkout's UI/node_modules
$wtRoot = Join-Path $repo '.claude\worktrees'

function Add-Worktree([string]$Name) {
    $path = Join-Path $wtRoot $Name
    Git $repo @('worktree', 'add', '--quiet', '-b', "feature/$Name", $path) | Out-Null
    return $path
}

try {
    New-Item -ItemType Directory -Force $repo, $modules | Out-Null
    Write-File (Join-Path $modules 'react\index.js') "precious`n"
    Git $repo @('init', '--quiet', '-b', 'master') | Out-Null
    Git $repo @('config', 'user.email', 'remove@example.invalid') | Out-Null
    Git $repo @('config', 'user.name', 'remove') | Out-Null
    Git $repo @('config', 'core.hooksPath', (Join-Path $repo 'no-hooks')) | Out-Null
    Write-File (Join-Path $repo '.gitignore') ".claude/`nnode_modules/`nbin/`nobj/`n"
    Write-File (Join-Path $repo 'README.txt') "main`n"
    Write-File (Join-Path $repo 'UI\package.json') "{}`n"
    Git $repo @('add', '--all') | Out-Null
    Git $repo @('commit', '--quiet', '-m', 'base') | Out-Null

    Write-Host '--- a clean worktree with a linked UI/node_modules and gitignored build output ---'
    $linked = Add-Worktree 'linked'
    New-Link (Join-Path $linked 'UI\node_modules') $modules
    Write-File (Join-Path $linked 'Mod\bin\Release\FiscalOverhaul.dll') "built`n"
    Check (Test-Path (Join-Path $linked 'UI\node_modules\react\index.js')) 'setup: the worktree sees the packages through its link'
    $run = Invoke-Remove $repo @('-Path', $linked)
    Check ($run.ExitCode -eq 0 -and -not (Test-Listed $linked) -and -not (Test-Path -LiteralPath $linked)) 'the clean worktree is removed, build output and all' $run.Output
    Check ($run.Output -match 'unlinked UI\\node_modules') 'it reports unlinking the node_modules link' $run.Output
    Check (Test-Path (Join-Path $modules 'react\index.js')) 'the folder the link pointed at survives'

    Write-Host '--- a worktree with a real (unlinked) node_modules folder ---'
    $real = Add-Worktree 'real'
    Write-File (Join-Path $real 'UI\node_modules\local\index.js') "local copy`n"
    $run = Invoke-Remove $repo @('-Path', $real)
    Check ($run.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $real)) 'a worktree with a real, gitignored node_modules is removed' $run.Output
    Check ($run.Output -notmatch 'unlinked') '... and nothing in it is reported as a link' $run.Output

    Write-Host '--- a dirty worktree with a link, not forced ---'
    $dirty = Add-Worktree 'dirty'
    New-Link (Join-Path $dirty 'UI\node_modules') $modules
    Write-File (Join-Path $dirty 'wip.txt') "unsaved`n"
    $refused = Invoke-Remove $repo @('-Path', $dirty)
    Check ($refused.ExitCode -ne 0 -and (Test-Listed $dirty) -and (Test-Path (Join-Path $dirty 'wip.txt'))) 'a dirty worktree is refused without -Force' $refused.Output
    Check (Test-Path (Join-Path $dirty 'UI\node_modules\react\index.js')) '... and its link still reaches the packages' $refused.Output
    $forced = Invoke-Remove $repo @('-Path', $dirty, '-Force')
    Check ($forced.ExitCode -eq 0 -and -not (Test-Listed $dirty) -and -not (Test-Path -LiteralPath $dirty)) 'with -Force the dirty worktree is removed' $forced.Output
    Check (Test-Path (Join-Path $modules 'react\index.js')) '... and the packages survive it'

    Write-Host '--- paths that are not worktrees ---'
    $missing = Invoke-Remove $repo @('-Path', (Join-Path $wtRoot 'never-existed'))
    Check ($missing.ExitCode -ne 0) 'a path that does not exist fails' $missing.Output
    $notWorktree = Join-Path $root 'plain-folder'
    Write-File (Join-Path $notWorktree 'file.txt') "not a worktree`n"
    $plain = Invoke-Remove $repo @('-Path', $notWorktree)
    Check ($plain.ExitCode -ne 0 -and (Test-Path (Join-Path $notWorktree 'file.txt'))) 'a folder that is not a worktree fails and is left alone' $plain.Output
    $mainRefused = Invoke-Remove $repo @('-Path', $repo)
    Check ($mainRefused.ExitCode -ne 0 -and (Test-Path (Join-Path $repo 'README.txt'))) 'the main working tree is never removed' $mainRefused.Output
}
finally {
    Set-Location $toolsDir
    foreach ($wt in Get-ChildItem $wtRoot -Directory -ErrorAction SilentlyContinue) {
        $link = Get-Item -LiteralPath (Join-Path $wt.FullName 'UI\node_modules') -Force -ErrorAction SilentlyContinue
        if ($link -and $link.LinkType) { $link.Delete() }
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All remove-worktree checks passed.' -ForegroundColor Green
exit 0
