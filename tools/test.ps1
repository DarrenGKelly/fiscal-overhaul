<#
.SYNOPSIS
    Runs the unit tests: the C# xunit tests (dotnet test Tests) and the UI's vitest tests (npm test in UI/).

.DESCRIPTION
    Neither needs the game, so any session may run them at any time. The C# run passes a temporary
    LocalModsPath, so a Tests project that references Mod/ never deploys into the game's Mods folder. The UI run sets
    CI=true, so vitest runs once instead of watching. A fresh worktree's UI packages are installed first (npm ci,
    or npm install without a lockfile).

    A part that doesn't exist yet (no Tests/ project, no UI/package.json or no "test" script in it) is skipped with
    a note, and the summary says what ran. Naming that part with -CSharpOnly or -UIOnly makes its absence a failure.

.EXAMPLE
    ./tools/test.ps1
    ./tools/test.ps1 -CSharpOnly -Filter "FullyQualifiedName~DensityTax"
    ./tools/test.ps1 -UIOnly

.NOTES
    Exit 0 = everything that ran passed (or nothing existed to run); 1 = a test run failed, or the part asked for
    doesn't exist; 2 = bad arguments. Tests: ./tools/test-test.ps1
#>
param(
    [switch]$CSharpOnly,
    [switch]$UIOnly,
    # dotnet test --filter expression (C# only).
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Write-Test([string]$Message, [string]$Colour = 'Gray') { Write-Host "test: $Message" -ForegroundColor $Colour }

if ($CSharpOnly -and $UIOnly) { Write-Test 'Pick at most one of -CSharpOnly and -UIOnly.' 'Red'; exit 2 }
$repo = ConvertTo-NormalizedPath (Join-Path $PSScriptRoot '..')
# The per-user node, when present (tools/git-common.ps1). The tests don't build the mod, so no DOTNET_ROOT.
Use-LocalToolchain
$results = [ordered]@{}
$failed = $false

function Invoke-Run([string]$At, [string]$Exe, [string[]]$Arguments) {
    Write-Test "$Exe $($Arguments -join ' ')"
    Push-Location -LiteralPath $At
    # Continue: Windows PowerShell 5.1 turns a native tool's redirected stderr into a terminating error under Stop.
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host $_ }; return $LASTEXITCODE }
    finally { Pop-Location; $ErrorActionPreference = 'Stop' }
}

# --- C#: xunit ---
if (-not $UIOnly) {
    $tests = Join-Path $repo 'Tests'
    $project = Get-ChildItem -LiteralPath $tests -Filter '*.csproj' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $project) {
        $results['C#'] = 'skipped: no Tests/*.csproj yet'
        Write-Test 'No Tests/*.csproj yet: no C# tests to run.' 'Yellow'
        if ($CSharpOnly) { $failed = $true }
    }
    else {
        $scratch = Join-Path ([IO.Path]::GetTempPath()) ('fo-test-mods-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Force $scratch | Out-Null
        $arguments = @('test', $project.FullName, '-nologo', "-p:LocalModsPath=$scratch")
        if ($Filter) { $arguments += @('--filter', $Filter) }
        try { $code = Invoke-Run $repo 'dotnet' $arguments }
        finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
        $results['C#'] = if ($code -eq 0) { 'passed' } else { "FAILED (exit $code)" }
        if ($code -ne 0) { $failed = $true }
    }
}

# --- UI: vitest ---
if (-not $CSharpOnly) {
    $ui = Join-Path $repo 'UI'
    $package = Join-Path $ui 'package.json'
    $hasTest = (Test-Path -LiteralPath $package) -and ((Get-Content -Raw -LiteralPath $package | ConvertFrom-Json).scripts.test)
    if (-not $hasTest) {
        $why = if (Test-Path -LiteralPath $package) { 'UI/package.json has no "test" script' } else { 'no UI/package.json yet' }
        $results['UI'] = "skipped: $why"
        Write-Test "$($why.Substring(0, 1).ToUpper() + $why.Substring(1)): no UI tests to run." 'Yellow'
        if ($UIOnly) { $failed = $true }
    }
    else {
        $npm = Find-Application 'npm.cmd' @('C:\Program Files\nodejs\npm.cmd')
        if (-not $npm) { Write-Test 'npm not found (install Node.js LTS).' 'Red'; exit 1 }
        $code = 0
        if (-not (Test-Path -LiteralPath (Join-Path $ui 'node_modules'))) {
            $install = if (Test-Path -LiteralPath (Join-Path $ui 'package-lock.json')) { 'ci' } else { 'install' }
            $code = Invoke-Run $ui $npm @($install)
        }
        if ($code -eq 0) {
            $previousCi = $env:CI
            $env:CI = 'true'
            try { $code = Invoke-Run $ui $npm @('test') }
            finally { $env:CI = $previousCi }
        }
        $results['UI'] = if ($code -eq 0) { 'passed' } else { "FAILED (exit $code)" }
        if ($code -ne 0) { $failed = $true }
    }
}

foreach ($part in $results.Keys) {
    $colour = if ($results[$part] -like 'FAILED*') { 'Red' } elseif ($results[$part] -like 'skipped*') { 'Yellow' } else { 'Green' }
    Write-Test ("{0,-3} {1}" -f $part, $results[$part]) $colour
}
if ($failed) { exit 1 }
exit 0
