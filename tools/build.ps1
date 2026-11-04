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

    -Ref <ref> builds that ref (origin/autodev, origin/master, origin/<branch>) instead of this checkout: it fetches,
    then checks the ref out detached in the dedicated build worktree (<main checkout>/.claude/worktrees/build) and
    builds there with this script's logic, so the user's checkout is never touched and an older ref still builds.
    build-info.txt then names the ref.

    -Zip packages instead of deploying: the full mod (C# and UI) is built into a temporary user-data folder, never the
    game's, and zipped to <main checkout>/Builds/FiscalOverhaul-<UI/mod.json version>-<commit>.zip. So it works with
    the game running, and the zip unpacks into a Mods folder as FiscalOverhaul/.

    The Mods folder is shared by every worktree, and the game locks the mod's DLL while it runs: deploying is part
    of the one shared game instance (knowledge/orchestration.md#the-shared-game-instance). So a deploying C# build
    refuses while Cities2.exe runs. -NoDeploy is the compile check any session may run at any time: the C# build
    goes to a temporary Mods folder with -p:SkipUI=true, and the UI is type-checked (tsc --noEmit) instead of built.

.EXAMPLE
    ./tools/build.ps1                # both parts, deployed
    ./tools/build.ps1 -UIOnly        # just the UI (the game may stay open)
    ./tools/build.ps1 -NoDeploy      # compile check: nothing reaches the Mods folder
    ./tools/build.ps1 -Ref origin/autodev        # the latest autodev, deployed for the user to play
    ./tools/build.ps1 -Ref origin/master -Zip    # a zip of master to send

.NOTES
    Exit 0 = every part built; 1 = a build failed; 2 = refused (the game is running, or a part named with -ModOnly
    or -UIOnly doesn't exist, an unknown -Ref, or -Zip with -ModOnly/-UIOnly/-NoDeploy). A missing UI/package.json is skipped with a note unless -UIOnly asks for it.
#>
param(
    [switch]$ModOnly,
    [switch]$UIOnly,
    [switch]$NoDeploy,
    [string]$Ref,
    [switch]$Zip,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Write-Build([string]$Message, [string]$Colour = 'Gray') { Write-Host "build: $Message" -ForegroundColor $Colour }

if ($ModOnly -and $UIOnly) { Write-Build 'Pick at most one of -ModOnly and -UIOnly.' 'Red'; exit 2 }
if ($Zip -and ($ModOnly -or $UIOnly -or $NoDeploy)) { Write-Build '-Zip builds the whole mod without deploying: drop -ModOnly, -UIOnly and -NoDeploy.' 'Red'; exit 2 }
$repo = ConvertTo-NormalizedPath (Join-Path $PSScriptRoot '..')
# The main checkout (not a worktree) holds the build worktree and Builds/.
$main = Split-Path -Parent (Invoke-Git -Path $repo -Arguments @('rev-parse', '--path-format=absolute', '--git-common-dir')).Text
$label = $null
if ($Ref) {
    Invoke-Git -Path $main -Arguments @('fetch', '-q', 'origin') | Out-Null
    $sha = (Invoke-Git -Path $main -Arguments @('rev-parse', '--verify', '--quiet', "$Ref^{commit}") -AllowFailure).Text
    if (-not $sha) { Write-Build "Unknown ref '$Ref' (name it as origin/<branch>)." 'Red'; exit 2 }
    $worktree = Join-Path $main '.claude\worktrees\build'
    if (Test-Path -LiteralPath (Join-Path $worktree '.git')) {
        Invoke-Git -Path $worktree -Arguments @('checkout', '-q', '--force', '--detach', $sha) | Out-Null
    }
    else { Invoke-Git -Path $main -Arguments @('worktree', 'add', '-q', '--detach', $worktree, $sha) | Out-Null }
    Write-Build "Building $Ref ($($sha.Substring(0, 10))) in $worktree."
    $repo = ConvertTo-NormalizedPath $worktree
    $label = $Ref
}
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

# A deploying C# build refuses before any slow step while the game runs.
if (-not $UIOnly -and -not $NoDeploy -and -not $Zip -and ($game = Get-GameProcess)) {
    Write-Build "Cities: Skylines II is running (pid $($game.Id)): it holds the mod's DLL, and the game is one shared instance. Close it (or use -NoDeploy for a compile check, -Zip for a package) and run this again." 'Red'
    exit 2
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
    elseif ($Zip) {
        # A temporary user-data folder: DeployWIP writes the C# side to its Mods\, webpack (CSII_USERDATAPATH) the UI.
        $package = Join-Path ([IO.Path]::GetTempPath()) ('fo-package-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Force (Join-Path $package 'Mods') | Out-Null
        $arguments += "-p:LocalModsPath=$(Join-Path $package 'Mods')"
        $env:CSII_USERDATAPATH = $package
    }
    $step = if ($Zip) { 'Building the C# mod, then the UI, into a temporary folder' } elseif ($withUI) { 'Building and deploying the C# mod, then the UI' } else { 'Building the C# mod' }
    Invoke-Step $step $repo 'dotnet' $arguments
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

function Get-BuildInfo([string[]]$Parts) {
    $branch = $label
    if (-not $branch) { $branch = (Invoke-Git -Path $repo -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure).Text }
    if (-not $branch) { $branch = 'detached' }
    $commit = (Invoke-Git -Path $repo -Arguments @('rev-parse', '--short=10', 'HEAD') -AllowFailure).Text
    $dirty = (Invoke-Git -Path $repo -Arguments @('status', '--porcelain', '--untracked-files=no') -AllowFailure).Text
    $shown = if ($dirty) { "$commit + uncommitted changes" } else { $commit }
    [pscustomobject]@{
        Branch = $branch; Commit = $commit; Shown = $shown
        Lines  = @("branch $branch", "commit $shown", "built $(Get-Date -Format 'yyyy-MM-dd HH:mm')", "parts $($Parts -join '+')", "from $repo")
    }
}

# --- the package (-Zip) ---
if ($Zip) {
    $folder = Join-Path $package 'Mods\FiscalOverhaul'
    if (-not (Test-Path -LiteralPath (Join-Path $folder 'FiscalOverhaul.mjs'))) { Write-Build "The UI module is missing from $folder." 'Red'; exit 1 }
    $info = Get-BuildInfo $built
    [IO.File]::WriteAllText((Join-Path $folder 'build-info.txt'), (($info.Lines -join "`n") + "`n"))
    $version = (Get-Content -Raw -LiteralPath (Join-Path $repo 'UI\mod.json') | ConvertFrom-Json).version
    $out = Join-Path $main 'Builds'
    New-Item -ItemType Directory -Force $out | Out-Null
    $zipPath = Join-Path $out "FiscalOverhaul-$version-$($info.Commit).zip"
    Compress-Archive -Path $folder -DestinationPath $zipPath -Force
    Remove-Item -LiteralPath $package -Recurse -Force -ErrorAction SilentlyContinue
    Write-Build "Packaged $zipPath ($($info.Branch) at $($info.Shown))." 'Green'
    exit 0
}

# --- what was deployed ---
if (-not $NoDeploy -and $built.Count) {
    $mods = [Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH', 'User')
    if (-not $mods) { $mods = $env:CSII_LOCALMODSPATH }
    $folder = if ($mods) { Join-Path $mods 'FiscalOverhaul' } else { $null }
    if ($folder -and (Test-Path -LiteralPath $folder)) {
        $info = Get-BuildInfo $built
        [IO.File]::WriteAllText((Join-Path $folder 'build-info.txt'), (($info.Lines -join "`n") + "`n"))
        Write-Build "Deployed to $folder ($($info.Branch) at $($info.Shown))." 'Green'
    }
    else { Write-Build "Built $($built -join ' and '); no deployed mod folder found to stamp (CSII_LOCALMODSPATH: '$mods')." 'Yellow' }
}
elseif ($built.Count) { Write-Build "Compile check passed: $($built -join ' and ') (nothing deployed)." 'Green' }
exit 0
