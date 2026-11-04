<#
.SYNOPSIS
    Builds the mod and deploys it into the game's Mods folder: the C# mod (Mod/), then the UI module (UI/).

.DESCRIPTION
    Default: dotnet build Mod/FiscalOverhaul.csproj -c Release. The csiimod toolchain's DeployWIP target wipes and
    refills %CSII_LOCALMODSPATH%\FiscalOverhaul, and the project's BuildUIAfterDeploy target then runs npm run build
    in UI/ (the webpack build writes the UI module into that same folder). So one build deploys both parts.
    UI packages are installed first (npm ci, or npm install without a lockfile) when UI/node_modules is missing, as
    in a fresh worktree; without them BuildUIAfterDeploy would skip the UI silently.

    -ModOnly passes -p:SkipUI=true: the C# mod alone. The deploy still wipes the folder, so the deployed mod then has
    no UI module until the next full or -UIOnly build. -UIOnly is npm run build in UI/ (the game may stay open).

    The per-user toolchain (newest %LOCALAPPDATA%\fiscal-overhaul-tools\node-v*, and dotnet6 as DOTNET_ROOT, which
    the game's ModPostProcessor.exe needs) is used when present (Use-LocalToolchain in tools/git-common.ps1).

    Finally writes build-info.txt (branch, commit, time, parts) into the deployed mod folder, so anyone can tell what
    the game will load (tools/check-batch-status.ps1 reports it).

    The Mods folder is shared by every worktree, and the game locks the mod's DLL while it runs: deploying is part
    of the one shared game instance (knowledge/orchestration.md#the-shared-game-instance). So a deploying C# build
    refuses while Cities2.exe runs. -NoDeploy is the compile check any session may run at any time: the C# build
    goes to a temporary Mods folder with -p:SkipUI=true, and the UI is type-checked (tsc --noEmit) instead of built.

.EXAMPLE
    ./tools/build.ps1                # both parts, deployed
    ./tools/build.ps1 -UIOnly        # just the UI (the game may stay open)
    ./tools/build.ps1 -NoDeploy      # compile check: nothing reaches the Mods folder

.NOTES
    Exit 0 = every part built; 1 = a build failed; 2 = refused (the game is running, or a part named with -ModOnly
    or -UIOnly doesn't exist). A missing UI/package.json is skipped with a note unless -UIOnly asks for it.
#>
param(
    [switch]$ModOnly,
    [switch]$UIOnly,
    [switch]$NoDeploy,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Write-Build([string]$Message, [string]$Colour = 'Gray') { Write-Host "build: $Message" -ForegroundColor $Colour }

if ($ModOnly -and $UIOnly) { Write-Build 'Pick at most one of -ModOnly and -UIOnly.' 'Red'; exit 2 }
$repo = ConvertTo-NormalizedPath (Join-Path $PSScriptRoot '..')
$doMod = -not $UIOnly
$doUI = -not $ModOnly
$built = @()

function Invoke-Step([string]$What, [string]$At, [string]$Exe, [string[]]$Arguments) {
    Write-Build "$What ($Exe $($Arguments -join ' '))"
    Push-Location -LiteralPath $At
    # Continue: Windows PowerShell 5.1 turns a native tool's redirected stderr into a terminating error under Stop.
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host $_ }; $code = $LASTEXITCODE }
    finally { Pop-Location; $ErrorActionPreference = 'Stop' }
    if ($code -ne 0) { Write-Build "$What failed (exit $code)." 'Red'; exit 1 }
}

Use-LocalToolchain -Dotnet6
$ui = Join-Path $repo 'UI'
$hasUI = Test-Path -LiteralPath (Join-Path $ui 'package.json')
if ($UIOnly -and -not $hasUI) { Write-Build 'No UI/package.json: there is no UI module to build.' 'Red'; exit 2 }
if ($doUI -and -not $hasUI) { Write-Build 'No UI/package.json yet: skipped the UI.' 'Yellow'; $doUI = $false }
$npm = $null
if ($doUI) {
    $npm = Find-Application 'npm.cmd' @('C:\Program Files\nodejs\npm.cmd')
    if (-not $npm) { Write-Build 'npm not found (install Node.js LTS, or unpack it under %LOCALAPPDATA%\fiscal-overhaul-tools).' 'Red'; exit 1 }
    if (-not (Test-Path -LiteralPath (Join-Path $ui 'node_modules'))) {
        $install = if (Test-Path -LiteralPath (Join-Path $ui 'package-lock.json')) { 'ci' } else { 'install' }
        Invoke-Step 'Installing the UI packages (none in this checkout yet)' $ui $npm @($install)
    }
}

# --- the C# mod (and, deploying, the UI after it: BuildUIAfterDeploy) ---
if ($doMod) {
    $project = Get-ChildItem -LiteralPath (Join-Path $repo 'Mod') -Filter '*.csproj' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $project) { Write-Build 'No Mod/*.csproj: nothing to build.' 'Red'; exit 2 }
    $arguments = @('build', $project.FullName, '-c', $Configuration, '-nologo')
    $withUI = $doUI -and -not $NoDeploy
    if (-not $withUI) { $arguments += '-p:SkipUI=true' }
    if ($NoDeploy) {
        $scratch = Join-Path ([IO.Path]::GetTempPath()) ('fo-build-mods-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Force $scratch | Out-Null
        $arguments += "-p:LocalModsPath=$scratch"
    }
    elseif ($game = Get-GameProcess) {
        Write-Build "Cities: Skylines II is running (pid $($game.Id)): it holds the mod's DLL, and the game is one shared instance. Close it (or use -NoDeploy for a compile check) and run this again." 'Red'
        exit 2
    }
    Invoke-Step $(if ($withUI) { 'Building and deploying the C# mod, then the UI' } else { 'Building the C# mod' }) $repo 'dotnet' $arguments
    if ($NoDeploy) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
    $built += 'Mod'
    if ($withUI) { $built += 'UI'; $doUI = $false }
}

# --- the UI module on its own (-UIOnly), or its type check (-NoDeploy) ---
if ($doUI) {
    if ($NoDeploy) {
        if (Test-Path -LiteralPath (Join-Path $ui 'tsconfig.json')) { Invoke-Step 'Type-checking the UI' $ui $npm @('exec', '--', 'tsc', '--noEmit') }
        else { Write-Build 'No UI/tsconfig.json: nothing to type-check without deploying.' 'Yellow' }
    }
    else { Invoke-Step 'Building the UI' $ui $npm @('run', 'build') }
    $built += 'UI'
}

# --- what was deployed ---
if (-not $NoDeploy -and $built.Count) {
    $mods = [Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH', 'User')
    if (-not $mods) { $mods = $env:CSII_LOCALMODSPATH }
    $folder = if ($mods) { Join-Path $mods 'FiscalOverhaul' } else { $null }
    if ($folder -and (Test-Path -LiteralPath $folder)) {
        $branch = (Invoke-Git -Path $repo -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure).Text
        if (-not $branch) { $branch = 'detached' }
        $commit = (Invoke-Git -Path $repo -Arguments @('rev-parse', '--short=10', 'HEAD') -AllowFailure).Text
        $dirty = (Invoke-Git -Path $repo -Arguments @('status', '--porcelain', '--untracked-files=no') -AllowFailure).Text
        if ($dirty) { $commit += ' + uncommitted changes' }
        $lines = @("branch $branch", "commit $commit", "built $(Get-Date -Format 'yyyy-MM-dd HH:mm')", "parts $($built -join '+')", "from $repo")
        [IO.File]::WriteAllText((Join-Path $folder 'build-info.txt'), (($lines -join "`n") + "`n"))
        Write-Build "Deployed to $folder ($branch at $commit)." 'Green'
    }
    else { Write-Build "Built $($built -join ' and '); no deployed mod folder found to stamp (CSII_LOCALMODSPATH: '$mods')." 'Yellow' }
}
elseif ($built.Count) { Write-Build "Compile check passed: $($built -join ' and ') (nothing deployed)." 'Green' }
exit 0
