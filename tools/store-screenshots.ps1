<#
.SYNOPSIS
    Takes the Paradox Mods screenshot set in the game, crops and scales it to spec, and installs it under new,
    version-suffixed names: Mod/Properties/Screenshots/, PublishConfiguration.xml's Screenshot entries and the README.

.DESCRIPTION
    knowledge/releasing.md#screenshots, the same way every time. Built on tools/game-run-common.ps1 (the claim, the
    Steam launch, the paused check and the quit that in-game-check.ps1 uses).

      1. Refuses (exit 2) unless the game is free ("Game: not running"). Deploys this checkout with tools/build.ps1
         (-Ref passes through; -SkipDeploy keeps the Mods folder).
      2. Sets the mod's settings file (FiscalOverhaul.coc in the game's LocalLow folder) for the shots - the chart,
         district recording, the services table, simulation changes and density taxes on, the density offsets -Offsets - after copying it aside; the copy
         goes back, byte for byte, once the game has exited (red or green). A copy left by a killed run is put back
         first.
      3. Launches through Steam, resumes the last save (use one with districts and a year of history: Odiham),
         makes sure it's paused (the PAUSED banner; Space only when it's missing), and opens Economy > Budget.
      4. Turns every Budget row on (the all-lines rule): reads each row's check square off a full-scale shot, clicks
         the ones that are off, and checks again. Then the shots, each a full-scale window capture:
           01-budget-chart          the cursor parked, every line on
           02-budget-tooltip        hovering ChartPoint
           05-budget-vanilla-detail hovering the Taxes row
           06-budget-district       one click on the district selector's right arrow (the first district)
           07-budget-district-list  the name box clicked: the list open (closed again, back to Entire City after)
           (the simulation runs -SimSeconds, then pauses again: the Taxation estimates need payers charged lately)
           03-taxation-density      Taxation, Residential's Density section open, showing the offsets
           09-taxation-district     the header picker's right arrow: Taxation in the first district's view (back after)
           08-services-table        Services, the Electricity row hovered: the table and the info column
           04-options               Escape, Escape, Options, Fiscal Overhaul, density taxes switched back off
      5. Quits the instance it launched (always, in a finally) and confirms "Game: not running".
      6. Crops each panel shot to the 16:9 box (285,193)-(2275,1312), the Options shot whole, scales to 1920x1080,
         and only when all nine exist installs them: <base>-v<X-Y-Z>.png in Mod/Properties/Screenshots/, the
         Screenshot entries and README links renamed (the page order kept), the old files of each base removed.
         The version is ModVersion, or the next patch after the highest v* tag when that tag is at or above it
         (-Version overrides): new names, so Paradox Mods doesn't serve its cached copies of the old ones.

    Every click on a point read off a reference image rather than clicked before (-ListPoints: Source "derived") is
    followed by a check screenshot in -CheckDir; a run that misses one is corrected with -Point Name=x,y.
    manifest.md in -CheckDir lists the shots, the check shots and the warnings (a running Discord, whose overlay can
    show in 04-options).

    -FromDir <folder> skips the game: it takes <base>.png full-scale (2560x1440) client captures from that folder and
    does step 6 only (re-crop a run's raw shots, or ones taken by hand).

.PARAMETER CheckDir
    Where the check shots, the raw full-scale captures (raw/) and manifest.md go. Default: a new folder under TEMP.
.PARAMETER Offsets
    The density offsets template: 12 integers, residential/commercial/industrial/office x low/medium/high. Default
    '2,1,-1,0,0,0,0,0,0,0,0,0'. A save keeps its own density rates, so this shows only on a save that has none yet;
    Odiham's own rates are what 03-taxation-density shows.
.PARAMETER SimSeconds
    How long the simulation runs before the Taxation shots (default 200): a full round of the tax engine's charges
    (16 ticks of 512 frames, a little over 2 minutes at 1x), so the estimates are settled rather than scaled up from
    the first groups charged. 0 skips the run (every estimate then reads 0).
.PARAMETER Version
    The X.Y.Z the new file names carry (default: see step 6).
.PARAMETER KeepDensityOn
    Leave density taxes checked on the Options shot (default: switched off there, as players first see it).
.PARAMETER RepoRoot
    The checkout whose Mod/Properties and README are rewritten (default: this script's). The tests point it at a
    fixture.
.PARAMETER DryRun
    Prints the version, the new names and the plan; touches nothing.

.EXAMPLE
    ./tools/store-screenshots.ps1
.EXAMPLE
    ./tools/store-screenshots.ps1 -SkipDeploy -Point OptionsModEntry=283,996 -CheckDir $env:TEMP\store
.EXAMPLE
    ./tools/store-screenshots.ps1 -FromDir $env:TEMP\store\raw

.NOTES
    Exit 0 = installed (or a clean dry run); 1 = the run failed, nothing installed, the settings file restored and the
    game closed; 2 = refused (the game in use, bad arguments); 3 = the game still runs after the run.
    Tests: ./tools/test-store-screenshots.ps1
#>
#Requires -Version 7.0
param(
    [string]$CheckDir,
    [switch]$SkipDeploy,
    [string]$Ref,
    [string]$Offsets = '2,1,-1,0,0,0,0,0,0,0,0,0',
    [ValidateRange(0, 600)][int]$SimSeconds = 200,
    [string]$Version,
    [string[]]$Point = @(),
    [switch]$KeepDensityOn,
    [string]$FromDir,
    [string]$RepoRoot,
    [string]$ModsPath,
    [switch]$DryRun,
    [switch]$ListPoints
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')
. (Join-Path $PSScriptRoot 'game-run-common.ps1')

function Write-Store([string]$Message, [string]$Colour = 'Gray') { Write-Host "store-screenshots: $Message" -ForegroundColor $Colour }
function Stop-Refused([string]$Message) { Write-Store $Message 'Red'; exit 2 }

# --- arguments ---
try { $points = Merge-GameRunPoints $Point } catch { Stop-Refused $_.Exception.Message }
if ($ListPoints) {
    foreach ($key in $points.Keys) { '{0,-20} {1,5},{2,-5} {3,-9} {4}' -f $key, $points[$key].X, $points[$key].Y, $points[$key].Source, $points[$key].What }
    exit 0
}
if ($Ref -and $SkipDeploy) { Stop-Refused '-Ref builds and deploys that ref; -SkipDeploy deploys nothing. Give one.' }
if ($FromDir -and ($Ref -or $SkipDeploy)) { Stop-Refused '-FromDir uses captures already taken: it neither deploys nor launches, so -Ref and -SkipDeploy mean nothing with it.' }
try { Set-StoreSettingsText '' $Offsets | Out-Null } catch { Stop-Refused $_.Exception.Message }
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot, (Get-Location).Path)
$configPath = Join-Path $RepoRoot 'Mod\Properties\PublishConfiguration.xml'
$readmePath = Join-Path $RepoRoot 'README.md'
$shotsDir = Join-Path $RepoRoot 'Mod\Properties\Screenshots'
if (-not (Test-Path -LiteralPath $configPath)) { Stop-Refused "no $configPath`: -RepoRoot must be a Fiscal Overhaul checkout." }
$configText = [IO.File]::ReadAllText($configPath)
$declared = if ($configText -match '<ModVersion\s+Value="([^"]*)"') { $Matches[1] } else { '' }
$tags = @((Invoke-Git -Path $RepoRoot -Arguments @('tag', '--list', 'v*') -AllowFailure).Text -split "`n" | Where-Object { $_.Trim() })
try { $shotVersion = Get-ScreenshotVersion $declared $tags $Version } catch { Stop-Refused $_.Exception.Message }
$newNames = @{}
foreach ($shot in $StoreShotSet) { $newNames[$shot.Base] = Get-StoreFileName $shot.Base $shotVersion }
if ($FromDir) {
    $FromDir = [IO.Path]::GetFullPath($FromDir, (Get-Location).Path)
    $missing = @($StoreShotSet | Where-Object { -not (Test-Path -LiteralPath (Join-Path $FromDir "$($_.Base).png")) } | ForEach-Object { "$($_.Base).png" })
    if ($missing) { Stop-Refused "-FromDir $FromDir lacks $($missing -join ', '): it needs all $($StoreShotSet.Count) full-scale captures." }
}
if (-not $CheckDir) { $CheckDir = Join-Path ([IO.Path]::GetTempPath()) ('fo-store-screenshots-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$CheckDir = [IO.Path]::GetFullPath($CheckDir, (Get-Location).Path)
$rawDir = if ($FromDir) { $FromDir } else { Join-Path $CheckDir 'raw' }

$tagNote = if ($Version) { '-Version' } elseif ($shotVersion -ne $declared) { "ModVersion $declared is tagged; the next patch" } else { "ModVersion $declared" }
Write-Store "version $shotVersion ($tagNote); names: $(($StoreShotSet | ForEach-Object { $newNames[$_.Base] }) -join ', ')"
if ($DryRun) {
    Write-Store "dry run: check shots and raw captures in $CheckDir; $(if ($FromDir) { "captures from $FromDir" } else { 'captures from the game' }); installs into $shotsDir. Nothing done."
    exit 0
}

# --- install: crop and scale, then move into place and rename the references (the whole set, or nothing) ---
function Install-StoreSet {
    $staging = Join-Path $CheckDir 'final'
    New-Item -ItemType Directory -Force $staging | Out-Null
    foreach ($shot in $StoreShotSet) {
        $box = if ($shot.Kind -eq 'panel') { $StoreCropBox } else { $null }
        Save-CroppedImage (Join-Path $rawDir "$($shot.Base).png") (Join-Path $staging $newNames[$shot.Base]) $box $StoreImageWidth $StoreImageHeight
    }
    New-Item -ItemType Directory -Force $shotsDir | Out-Null
    foreach ($shot in $StoreShotSet) { Copy-Item -LiteralPath (Join-Path $staging $newNames[$shot.Base]) -Destination (Join-Path $shotsDir $newNames[$shot.Base]) -Force }
    $removed = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $shotsDir -Filter '*.png' -File)) {
        $base = Get-StoreBase $file.Name
        if ($newNames.ContainsKey($base) -and $file.Name -ne $newNames[$base]) { Remove-Item -LiteralPath $file.FullName; $removed += $file.Name }
    }
    $newConfig = Update-StoreConfigText $configText $newNames
    Write-TextKeepingBom $configPath $newConfig
    $listed = @([regex]::Matches($newConfig, '<Screenshot\s+Value="Properties/Screenshots/([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $unlisted = @($StoreShotSet | Where-Object { $listed -notcontains $newNames[$_.Base] } | ForEach-Object { $newNames[$_.Base] })
    if (Test-Path -LiteralPath $readmePath) {
        Write-TextKeepingBom $readmePath (Update-StoreReadmeText ([IO.File]::ReadAllText($readmePath)) $newNames)
    }
    Write-Store "installed $($StoreShotSet.Count) screenshots in $shotsDir; removed $(if ($removed) { $removed -join ', ' } else { 'nothing' })." 'Green'
    if ($unlisted) { Write-Store "not listed in PublishConfiguration.xml (add a <Screenshot> for each where the page should show it): $($unlisted -join ', ')" 'Yellow' }
}

if ($FromDir) {
    try { Install-StoreSet } catch { Write-Store "failed: $($_.Exception.Message)" 'Red'; exit 1 }
    exit 0
}

# --- claim ---
$claim = Get-GameClaimProblem
if ($claim) { Stop-Refused $claim }
Write-Store 'Game: not running - claimed for this run.'
New-Item -ItemType Directory -Force $CheckDir, $rawDir | Out-Null

$settingsPath = Join-Path (Get-GameDataDir) 'FiscalOverhaul.coc'
$backupPath = "$settingsPath.store-screenshots-backup"
$absentMarker = "$settingsPath.store-screenshots-absent"
# A killed run's copy goes back before anything else.
if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $settingsPath -Force; Write-Store "restored $settingsPath from a previous run's copy." 'Yellow' }
elseif (Test-Path -LiteralPath $absentMarker) { Remove-Item -LiteralPath $settingsPath -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $absentMarker; Write-Store "removed the settings file a previous run created." 'Yellow' }

$warnings = New-Object System.Collections.Generic.List[string]
if (Get-Process -Name 'Discord*' -ErrorAction SilentlyContinue) {
    $warnings.Add('Discord is running: its overlay (the voice list at the left edge, a toast top left) can show in 04-options, which is the whole window. Check it; if it shows, turn the overlay off in Discord and run again.')
}
foreach ($w in $warnings) { Write-Store $w 'Yellow' }

$checks = New-Object System.Collections.Generic.List[object]
function Save-Check($Session, [string]$Name, [string]$What, [int]$Delay = 1200) {
    Save-GameShot -Out (Join-Path $CheckDir "$Name.png") -Delay $Delay
    $checks.Add([pscustomobject]@{ Name = "$Name.png"; What = $What })
}
function Save-Raw([string]$Base, [int]$Delay = 1500) { Save-GameShot -Out (Join-Path $rawDir "$Base.png") -Scale 1 -Delay $Delay }

$session = New-GameRunSession $points
$result = 'red'
$failure = ''
$exitCode = 1
$quitEscapes = 2
$settingsTouched = $false
try {
    # --- deploy ---
    if (-not $SkipDeploy) {
        $buildArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'build.ps1'))
        if ($Ref) { $buildArgs += @('-Ref', $Ref) }
        & (Get-Process -Id $PID).Path @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "tools/build.ps1 failed (exit $LASTEXITCODE): nothing launched" }
    }
    $deployed = Get-DeployedBuildInfo $ModsPath
    Write-Store "deployed: $($deployed -join ', ')"

    # --- the settings the shots need ---
    $existing = if (Test-Path -LiteralPath $settingsPath) { [IO.File]::ReadAllText($settingsPath) } else { $null }
    if ($null -ne $existing) { Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force }
    else { New-Item -ItemType Directory -Force (Split-Path -Parent $settingsPath) | Out-Null; [IO.File]::WriteAllText($absentMarker, '') }
    $settingsTouched = $true
    [IO.File]::WriteAllText($settingsPath, (Set-StoreSettingsText $existing $Offsets), (New-Object System.Text.UTF8Encoding($false)))
    Write-Store "set $settingsPath for the shots (density taxes on, offsets $Offsets); the original goes back after the game exits."

    # --- launch, load, pause, the Budget tab ---
    Start-GameRun $session $CheckDir
    Assert-GamePaused $session $CheckDir
    Invoke-GameClick $session 'MoneyTotal'
    Invoke-GameMove $session 'Neutral'

    # --- every row on ---
    $full = Join-Path $CheckDir 'rows-before.png'
    Save-GameShot -Out $full -Scale 1 -Delay 1500
    $off = Get-UncheckedRows $full $points
    Write-Store "rows off at first: $(if ($off) { $off -join ', ' } else { 'none' })"
    foreach ($row in $off) { Invoke-GameClick $session $row; Start-Sleep -Milliseconds 300 }
    Invoke-GameMove $session 'Neutral'
    $full = Join-Path $CheckDir 'rows-on.png'
    Save-GameShot -Out $full -Scale 1 -Delay 1500
    $checks.Add([pscustomobject]@{ Name = 'rows-on.png'; What = 'full scale: every Budget row''s check square filled' })
    $still = Get-UncheckedRows $full $points
    if ($still) { throw "rows still off after clicking them: $($still -join ', '). Look at rows-on.png and correct their points (-Point RowX=x,y; -ListPoints)." }

    # --- the Budget shots ---
    Save-Raw '01-budget-chart'
    Invoke-GameMove $session 'ChartPoint' -Hover
    Save-Raw '02-budget-tooltip'
    Invoke-GameMove $session 'TaxesHover' -Hover
    Save-Raw '05-budget-vanilla-detail'
    Invoke-GameMove $session 'Neutral'
    Invoke-GameClick $session 'DistrictNext'
    Invoke-GameMove $session 'Neutral'
    Save-Raw '06-budget-district'
    Invoke-GameClick $session 'DistrictName'
    Invoke-GameMove $session 'Neutral'
    Save-Raw '07-budget-district-list'
    Invoke-GameClick $session 'DistrictName'
    Invoke-GameClick $session 'DistrictPrev'
    Invoke-GameMove $session 'Neutral'
    Save-Check $session 'district-back' 'the list closed and Entire City selected again'

    # --- Taxation ---
    if ($SimSeconds -gt 0) {
        Write-Store "running the simulation $SimSeconds s for the Taxation estimates"
        Invoke-SimulationRun $session $CheckDir $SimSeconds | Out-Null
        $checks.Add([pscustomobject]@{ Name = 'sim-running.png'; What = 'no PAUSED banner: the simulation running' })
        $checks.Add([pscustomobject]@{ Name = 'sim-paused.png'; What = 'the PAUSED banner back after the run' })
    }
    Invoke-GameClick $session 'TaxationTab'
    Invoke-GameClick $session 'ResidentialDensity'
    Invoke-GameMove $session 'Neutral'
    Save-Raw '03-taxation-density'
    Invoke-GameClick $session 'DistrictNext'
    Invoke-GameMove $session 'Neutral'
    Save-Raw '09-taxation-district'
    Invoke-GameClick $session 'DistrictPrev'
    Invoke-GameMove $session 'Neutral'
    Save-Check $session 'taxation-back' 'Taxation back on the city view'

    # --- Services ---
    Invoke-GameClick $session 'ServicesTab'
    Invoke-GameMove $session 'ServiceElectricity' -Hover
    Save-Raw '08-services-table'

    # --- Options ---
    Invoke-GameControl focus | Out-Null
    Invoke-GameControl key Escape | Out-Null
    Start-Sleep -Milliseconds 700
    Invoke-GameControl key Escape | Out-Null
    $quitEscapes = 0
    Save-Check $session 'pause-menu' 'the pause menu: Options is the point PauseOptions'
    Invoke-GameClick $session 'PauseOptions'
    $quitEscapes = 1
    Save-Check $session 'options-open' 'Options open: FISCAL OVERHAUL in the left list is OptionsModEntry'
    Invoke-GameClick $session 'OptionsModEntry'
    Save-Check $session 'options-page' 'Options > Fiscal Overhaul: Enable density taxes (BETA) checked is OptionsDensity'
    if (-not $KeepDensityOn) {
        Invoke-GameClick $session 'OptionsDensity'
    }
    Invoke-GameMove $session 'OptionsNeutral'
    Save-Raw '04-options'
    $result = 'green'
    $exitCode = 0
}
catch {
    $failure = $_.Exception.Message
    Write-Store "failed: $failure" 'Red'
}
finally {
    $closed = $false
    try { $closed = Stop-GameRun $session $CheckDir -Escapes $quitEscapes -Force:($result -ne 'green') }
    catch { Write-Store "quitting failed: $($_.Exception.Message)" 'Red' }
    if (-not $closed) { $exitCode = 3 }
    if ($settingsTouched) {
        if ($closed) {
            if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $settingsPath -Force }
            elseif (Test-Path -LiteralPath $absentMarker) { Remove-Item -LiteralPath $settingsPath -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $absentMarker }
            Write-Store "restored $settingsPath."
        }
        else { Write-Store "the game still runs, so $settingsPath wasn't restored: the next run puts the copy back ($backupPath)." 'Red' }
    }
}

if ($result -eq 'green') {
    try { Install-StoreSet }
    catch { $result = 'red'; $failure = "install: $($_.Exception.Message)"; Write-Store "failed: $failure" 'Red'; if ($exitCode -eq 0) { $exitCode = 1 } }
}
$lines = @('# Store screenshots', '',
    "- Result: $result$(if ($failure) { " - $failure" })",
    "- Version: $shotVersion",
    "- Deployed: $((Get-DeployedBuildInfo $ModsPath) -join ', ')",
    "- Offsets: $Offsets", '',
    '| Shot | Shows |', '|---|---|')
foreach ($shot in $StoreShotSet) { $lines += "| $($newNames[$shot.Base]) | $($shot.What) |" }
$lines += @('', '| Check shot | Shows |', '|---|---|')
foreach ($c in $checks) { $lines += "| $($c.Name) | $($c.What) |" }
if ($warnings.Count) { $lines += @('', '## Warnings', ''); foreach ($w in $warnings) { $lines += "- $w" } }
[IO.File]::WriteAllText((Join-Path $CheckDir 'manifest.md'), (($lines -join "`n") + "`n"))
Write-Store "manifest: $(Join-Path $CheckDir 'manifest.md')"
exit $exitCode
