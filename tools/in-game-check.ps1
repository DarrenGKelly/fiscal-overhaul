<#
.SYNOPSIS
    The in-game check as one script: claim the game, deploy, launch through Steam, resume the last save, keep it
    paused, open Economy > Budget and Taxation, hover a line, write a fixed set of screenshots and a manifest, quit.

.DESCRIPTION
    knowledge/testing.md#the-in-game-check, the same way every time. Judging the images stays with the agent: this
    takes them and says what each should show.

      1. Refuses (exit 2) unless the game is free: check-batch-status's "Game: not running" (the same process check).
      2. Deploys the checkout it runs from with tools/build.ps1 (-Ref passes through); -SkipDeploy keeps what the Mods
         folder holds. The manifest names what was deployed (build-info.txt).
      3. Launches through Steam, clicks Resume in the Paradox launcher (the most recent save), waits for the load,
         refuses a client area other than 2560x1440 (every offset assumes it), and makes sure the game is paused
         from the PAUSED banner: Space only when it's missing, never blindly.
      4. Opens the Economy panel (the money total), screenshots Budget with the cursor parked, hovers a chart line,
         screenshots the tooltip, opens Taxation and screenshots it. Then runs -Steps, if given.
      5. Always (red or green, in a finally) quits the instance it launched - Escape, Escape, Exit, Exit to Desktop;
         stopped when that fails - and prints "Game: not running". It never closes an instance it didn't launch.

    The shots, in -Out (half-scale window shots, 1280x720):
      launcher.png   the Paradox launcher before Resume
      loaded.png     the loaded save, full scale (the paused check reads it)
      budget.png     Economy > Budget, the cursor off the rows: the chart and the rows
      budget-hover.png   the chart's tooltip at ChartPoint, with the Change/Average overlay
      taxation.png   Economy > Taxation: the vanilla rows, and the By density rows when density taxes are on
      <steps>.png    each -Steps screenshot
      quit-menu.png, quit-dialog.png   the pause menu and the Exit dialog on the way out
    and manifest.md: the deployed branch and commit, the save, each shot and what it shows, and how the run ended.

    -Steps <file>: extra per-plan steps, run after taxation.png with the Economy panel open on Taxation. One per line
    ('#' comments): click|right-click|move|hover <point>, key <name>, wait <ms>, screenshot <name> [what it shows].
    A <point> is @Name (the named points: -ListPoints) or x,y, client offsets in the 2560x1440 window. Space is
    refused (the pause). End with the panel open (or closed with key Escape once more and -QuitEscapes 1).

.PARAMETER Out
    The folder for the screenshots and manifest.md (created; existing files are overwritten).
.PARAMETER SkipDeploy
    Don't run tools/build.ps1: test whatever the Mods folder holds (the manifest still names it).
.PARAMETER Ref
    Passed to tools/build.ps1 -Ref: build and deploy that ref (origin/<branch>) instead of this checkout.
.PARAMETER Steps
    A steps file (above).
.PARAMETER Point
    Overrides a named point: Name=x,y (client offsets), repeatable. -ListPoints prints them.
.PARAMETER QuitEscapes
    Escape presses from the end of the run to the pause menu (default 2: the panel open).
.PARAMETER DryRun
    Checks the arguments and the steps file and prints the plan; claims nothing, launches nothing.

.EXAMPLE
    ./tools/in-game-check.ps1 -Out $env:TEMP\igc
.EXAMPLE
    ./tools/in-game-check.ps1 -Out $env:TEMP\igc -SkipDeploy -Steps plan-steps.txt -Point ChartPoint=1700,700

.NOTES
    Exit 0 = the shots were taken and the game is closed; 1 = the run failed (the manifest says where; the game is
    still closed); 2 = refused (the game is in use, bad arguments) - nothing launched; 3 = the game is still running
    after the run (stop it: Stop-Process -Name Cities2, if this run launched it).
    Tests: ./tools/test-in-game-check.ps1
#>
param(
    [string]$Out,
    [switch]$SkipDeploy,
    [string]$Ref,
    [string]$Steps,
    [string[]]$Point = @(),
    [int]$QuitEscapes = 2,
    [string]$ModsPath,
    [switch]$DryRun,
    [switch]$ListPoints
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')
. (Join-Path $PSScriptRoot 'game-run-common.ps1')

function Write-Check([string]$Message, [string]$Colour = 'Gray') { Write-Host "in-game-check: $Message" -ForegroundColor $Colour }
function Stop-Refused([string]$Message) { Write-Check $Message 'Red'; exit 2 }

# --- arguments ---
try { $points = Merge-GameRunPoints $Point } catch { Stop-Refused $_.Exception.Message }
if ($ListPoints) {
    foreach ($key in $points.Keys) { '{0,-20} {1,5},{2,-5} {3,-9} {4}' -f $key, $points[$key].X, $points[$key].Y, $points[$key].Source, $points[$key].What }
    exit 0
}
if (-not $Out) { Stop-Refused '-Out <folder> is required: where the screenshots and manifest.md go.' }
if ($Ref -and $SkipDeploy) { Stop-Refused '-Ref builds and deploys that ref; -SkipDeploy deploys nothing. Give one.' }
if ($QuitEscapes -lt 0 -or $QuitEscapes -gt 4) { Stop-Refused "-QuitEscapes must be 0 to 4, not $QuitEscapes." }
$extraSteps = @()
if ($Steps) {
    if (-not (Test-Path -LiteralPath $Steps -PathType Leaf)) { Stop-Refused "-Steps file '$Steps' doesn't exist." }
    try { $extraSteps = ConvertFrom-GameRunSteps @(Get-Content -LiteralPath $Steps) $points } catch { Stop-Refused $_.Exception.Message }
    $fixed = 'launcher', 'loaded', 'paused', 'budget', 'budget-hover', 'taxation', 'quit-menu', 'quit-dialog'
    $clash = @($extraSteps | Where-Object { $_.Verb -eq 'screenshot' -and $fixed -contains $_.Name })
    if ($clash) { Stop-Refused "-Steps screenshot '$($clash[0].Name)' would overwrite one of the fixed shots ($($fixed -join ', ')): rename it." }
}
$outDir = [IO.Path]::GetFullPath($Out, (Get-Location).Path)

$deploy = if ($SkipDeploy) { 'none (-SkipDeploy): whatever the Mods folder holds' } elseif ($Ref) { "tools/build.ps1 -Ref $Ref" } else { "tools/build.ps1 from $(Split-Path -Parent $PSScriptRoot)" }
if ($DryRun) {
    Write-Check "dry run: out $outDir; deploy $deploy; $($extraSteps.Count) extra step(s)."
    foreach ($s in $extraSteps) { Write-Host "  step $($s.Line): $($s.Verb) $(if ($s.Point) { $s.Point } elseif ($s.Key) { $s.Key } elseif ($s.Name) { "$($s.Name) - $($s.What)" } else { $s.Ms })" }
    exit 0
}

# --- claim ---
$claim = Get-GameClaimProblem
if ($claim) { Stop-Refused $claim }
Write-Check 'Game: not running - claimed for this run.'
New-Item -ItemType Directory -Force $outDir | Out-Null

$shots = New-Object System.Collections.Generic.List[object]
$session = New-GameRunSession $points
$result = 'red'
$failure = ''
$exitCode = 1
$deployed = @()
$save = ''
try {
    # --- deploy ---
    if (-not $SkipDeploy) {
        $buildArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'build.ps1'))
        if ($Ref) { $buildArgs += @('-Ref', $Ref) }
        Write-Check "deploying: $deploy"
        $shell = (Get-Process -Id $PID).Path
        & $shell @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "tools/build.ps1 failed (exit $LASTEXITCODE): nothing launched" }
    }
    $deployed = Get-DeployedBuildInfo $ModsPath
    Write-Check "deployed: $($deployed -join ', ')"

    # --- launch, load, pause ---
    Start-GameRun $session $outDir
    if (Test-Path -LiteralPath (Join-Path $outDir 'launcher.png')) { $shots.Add([pscustomobject]@{ Name = 'launcher.png'; What = 'the Paradox launcher before Resume' }) }
    Assert-GamePaused $session $outDir
    $shots.Add([pscustomobject]@{ Name = 'loaded.png'; What = 'the loaded save, full scale: the PAUSED banner top centre' })
    if (Test-Path -LiteralPath (Join-Path $outDir 'paused.png')) { $shots.Add([pscustomobject]@{ Name = 'paused.png'; What = 'after the one Space that paused it' }) }

    # --- the Economy panel ---
    Invoke-GameClick $session 'MoneyTotal'
    Invoke-GameMove $session 'Neutral'
    Save-GameShot -Out (Join-Path $outDir 'budget.png')
    $shots.Add([pscustomobject]@{ Name = 'budget.png'; What = 'Economy > Budget, cursor off the rows: the rows on the left, the line chart, the district selector above' })
    Invoke-GameMove $session 'ChartPoint' -Hover
    Save-GameShot -Out (Join-Path $outDir 'budget-hover.png')
    $shots.Add([pscustomobject]@{ Name = 'budget-hover.png'; What = 'the chart tooltip at ChartPoint (series, value/mo, age) and the Change/Average overlay top left' })
    Invoke-GameClick $session 'TaxationTab'
    Invoke-GameMove $session 'Neutral'
    Save-GameShot -Out (Join-Path $outDir 'taxation.png')
    $shots.Add([pscustomobject]@{ Name = 'taxation.png'; What = 'Economy > Taxation: the vanilla rows, plus By density rows when density taxes are on' })

    # --- the plan's own steps ---
    if ($extraSteps.Count) {
        Write-Check "running $($extraSteps.Count) step(s) from $Steps"
        foreach ($s in (Invoke-GameRunSteps $session $extraSteps $outDir)) { $shots.Add($s) }
    }
    $result = 'green'
    $exitCode = 0
}
catch {
    $failure = $_.Exception.Message
    Write-Check "failed: $failure" 'Red'
}
finally {
    $closed = $false
    try { $closed = Stop-GameRun $session $outDir -Escapes $QuitEscapes -Force:($result -ne 'green') }
    catch { Write-Check "quitting failed: $($_.Exception.Message)" 'Red' }
    if (-not $closed) { $exitCode = 3 }
    $wait = Join-Path (Join-Path (Get-GameDataDir) 'Logs') 'SceneFlow.log'
    if ((Test-Path -LiteralPath $wait) -and ((Get-Content -LiteralPath $wait -Raw -ErrorAction SilentlyContinue) -match "Starting game from '([^ ']+)")) { $save = $Matches[1] }
    foreach ($name in 'quit-menu.png', 'quit-dialog.png') {
        if (Test-Path -LiteralPath (Join-Path $outDir $name)) { $shots.Add([pscustomobject]@{ Name = $name; What = 'on the way out: the pause menu, then the Exit dialog' }) }
    }
    $lines = @('# In-game check', '',
        "- Result: $result$(if ($failure) { " - $failure" })",
        "- Deployed: $(if ($deployed) { $deployed -join ', ' } else { 'not read' }) ($deploy)",
        "- Save: $(if ($save) { $save } else { 'unknown' })",
        "- Taken: $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
        "- Game after the run: $(if ($closed) { 'not running' } else { 'STILL RUNNING' })", '',
        '| Shot | Shows |', '|---|---|')
    foreach ($s in $shots) { $lines += "| $($s.Name) | $($s.What) |" }
    [IO.File]::WriteAllText((Join-Path $outDir 'manifest.md'), (($lines -join "`n") + "`n"))
    Write-Check "manifest: $(Join-Path $outDir 'manifest.md')"
}
exit $exitCode
