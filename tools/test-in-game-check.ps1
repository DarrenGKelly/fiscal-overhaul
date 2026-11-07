<#
.SYNOPSIS
    Tests tools/in-game-check.ps1 and the shared tools/game-run-common.ps1 without the game.

.DESCRIPTION
    Never launches Steam or the game, never moves the cursor: FO_GAME_CONTROL names a stub game-control.ps1 (it logs
    each command, prints a 2560x1440 client rect at 100,50 and paints screenshots: the PAUSED banner, filled Budget
    check squares, or not, as FO_STUB_* say), FO_GAME_LAUNCH starts a renamed copy of ping.exe as the "game" and
    FO_GAME_PROCESS_NAME points every process check at it. What must hold, each with a pass and a fail case: the -Point
    overrides, the client-offset maths and the 2560x1440 guard, the steps grammar, the PAUSED-banner test, the deployed
    build-info; the argument refusals; the refusal while the game runs (nothing launched, that instance left alone); a
    green run takes the fixed shots at origin + offset, writes the manifest, never presses Space on a paused save and
    quits what it launched; an unpaused save gets exactly one Space; a failed run (red) still stops the instance it
    launched and says "Game: not running".
        ./tools/test-in-game-check.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')
. (Join-Path $PSScriptRoot 'game-run-common.ps1')

$failures = 0
function Check([bool]$Condition, [string]$Description, [string]$Detail = '') {
    if ($Condition) { Write-Host "ok   $Description" }
    else {
        Write-Host "FAIL $Description" -ForegroundColor Red
        if ($Detail) { Write-Host ($Detail.Trim() -replace '(?m)^', '       ') }
        $script:failures++
    }
}
function Throws([scriptblock]$Block, [string]$Pattern) {
    try { & $Block | Out-Null; return $false } catch { return $_.Exception.Message -match $Pattern }
}

$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
$root = Join-Path ([IO.Path]::GetTempPath()) "fo-test-in-game-check-$id"
$log = Join-Path $root 'calls.txt'
$shell = (Get-Process -Id $PID).Path
$fakeName = "fo-fake-game-$id"
$fakeExe = Join-Path $root "$fakeName.exe"
$savedNames = 'FO_GAME_CONTROL', 'FO_GAME_LAUNCH', 'FO_GAME_LAUNCH_ARGS', 'FO_GAME_PROCESS_NAME', 'FO_GAME_DATA_DIR', 'FO_STUB_LOG',
    'FO_STUB_FAIL_AT', 'FO_STUB_UNPAUSED', 'FO_STUB_ROWS_OFF', 'FO_STUB_EXIT_KILLS'

$Stub = @'
param([Parameter(Position = 0)][string]$Command, [Parameter(Position = 1)][string]$Name, [string]$Out, [double]$Scale = 1,
    [switch]$Window, [string]$Title, [int]$X, [int]$Y, [switch]$Right, [switch]$Double, [int]$Delay = 0, [int]$Timeout = 300, [switch]$Gone)
$line = $Command
if ($Name) { $line += " $Name" }
if ($PSBoundParameters.ContainsKey('X')) { $line += " $X,$Y" }
if ($Gone) { $line += ' -Gone' }
if ($Title) { $line += ' -Title' }
if ($Out) { $line += " $([IO.Path]::GetFileName($Out)) scale $Scale" }
Add-Content -LiteralPath $env:FO_STUB_LOG -Value $line
if ($env:FO_STUB_FAIL_AT -and $line -like $env:FO_STUB_FAIL_AT) { [Console]::Error.WriteLine("stub: failing '$line'"); exit 1 }
switch ($Command) {
    'window' { 'client: 100,50,2560,1440' }
    'screenshot' {
        Add-Type -AssemblyName System.Drawing
        $s = $Scale
        $b = [System.Drawing.Bitmap]::new([int](2560 * $s), [int](1440 * $s))
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.Clear([System.Drawing.Color]::FromArgb(60, 73, 91))
        $spaces = @(Get-Content -LiteralPath $env:FO_STUB_LOG | Where-Object { $_ -eq 'key Space' }).Count
        $paused = (-not $env:FO_STUB_UNPAUSED) -or ($env:FO_STUB_UNPAUSED -eq 'once' -and $spaces -gt 0)
        if ($paused) { $g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(74, 192, 240)), [int](1170 * $s), [int](4 * $s), [int](220 * $s), [int](44 * $s)) }
        if (-not $env:FO_STUB_ROWS_OFF) { $g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(113, 241, 168)), [int](370 * $s), [int](400 * $s), [int](40 * $s), [int](700 * $s)) }
        $g.Dispose()
        $b.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
        "screenshot: $Out"
    }
    'click' {
        if ($env:FO_STUB_EXIT_KILLS -and $X -eq 1378 -and $Y -eq 850) { Get-Process -Name $env:FO_GAME_PROCESS_NAME -ErrorAction SilentlyContinue | Stop-Process -Force }
        "click: $X,$Y"
    }
    'wait-process' {
        if ($Gone) {
            $clock = [Diagnostics.Stopwatch]::StartNew()
            while ((Get-Process -Name $env:FO_GAME_PROCESS_NAME -ErrorAction SilentlyContinue) -and $clock.Elapsed.TotalSeconds -lt 3) { Start-Sleep -Milliseconds 200 }
            if (Get-Process -Name $env:FO_GAME_PROCESS_NAME -ErrorAction SilentlyContinue) { [Console]::Error.WriteLine('stub: still running'); exit 1 }
            'wait-process: gone'
        }
        else { 'wait-process: up' }
    }
    default { "$Command ok" }
}
'@

function New-Bitmap([int]$W, [int]$H, [System.Drawing.Color]$Fill) {
    $b = [System.Drawing.Bitmap]::new($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear($Fill); $g.Dispose()
    return $b
}

function Invoke-Check([string[]]$Arguments, [hashtable]$Env = @{}) {
    Set-Content -LiteralPath $log -Value $null
    foreach ($name in 'FO_STUB_FAIL_AT', 'FO_STUB_UNPAUSED', 'FO_STUB_ROWS_OFF', 'FO_STUB_EXIT_KILLS') { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    foreach ($key in $Env.Keys) { Set-Item "Env:$key" $Env[$key] }
    $global:LASTEXITCODE = 0
    $ErrorActionPreference = 'Continue'
    try { $output = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'in-game-check.ps1') @Arguments 2>&1 | Out-String }
    finally { $ErrorActionPreference = 'Stop' }
    $calls = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log) -join "`n" } else { '' }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Calls = $calls }
}
function Test-FakeRunning { return [bool](Get-Process -Name $fakeName -ErrorAction SilentlyContinue) }

$preexisting = $null
try {
    New-Item -ItemType Directory -Force $root | Out-Null
    Add-Type -AssemblyName System.Drawing

    Write-Host '--- points ---'
    $p = Merge-GameRunPoints @('ChartPoint=1700,700')
    Check ($p.ChartPoint.X -eq 1700 -and $p.ChartPoint.Y -eq 700 -and $p.ChartPoint.Source -eq 'override') '-Point Name=x,y overrides a point'
    Check ($GameRunPoints.ChartPoint.X -eq 2045) '... on a copy: the table itself is unchanged'
    Check (Throws { Merge-GameRunPoints @('ChartPoint=1700') } "isn't Name=x,y") 'fail case: a malformed -Point is refused'
    Check (Throws { Merge-GameRunPoints @('Nowhere=1,2') } "no point named 'Nowhere'") 'fail case: an unknown point name is refused'
    $s = ConvertTo-ScreenPoint ([pscustomobject]@{ X = 100; Y = 50 }) 1950 1406
    Check ($s.X -eq 2050 -and $s.Y -eq 1456) 'screen = client origin + offset'
    $client = ConvertFrom-WindowOutput "window: 'Cities: Skylines II'`nrect:   0,0,2576,1479`nclient: 8,31,2560,1440`nfront:  True"
    Check ($client.X -eq 8 -and $client.Y -eq 31 -and $client.Width -eq 2560) 'the client rect is read from game-control window'
    Check ($null -eq (Get-ClientSizeProblem $client)) 'a 2560x1440 client passes'
    Check ((Get-ClientSizeProblem ([pscustomobject]@{ X = 0; Y = 0; Width = 1920; Height = 1080 })) -match 'not 2560x1440') 'fail case: another client size is refused (every offset assumes 2560x1440)'

    Write-Host '--- the steps grammar ---'
    $steps = ConvertFrom-GameRunSteps @('# a comment', 'click @TaxationTab', 'hover 1800,660   # the chart', 'key Escape', 'wait 500', 'screenshot after-escape the panel closed', '') (Merge-GameRunPoints)
    Check ($steps.Count -eq 5) 'comments and blank lines are skipped' "$($steps.Count) steps"
    Check ($steps[0].X -eq 1280 -and $steps[0].Y -eq 306 -and $steps[0].Point -eq 'TaxationTab') '@Name resolves to the named point''s offsets'
    Check ($steps[1].Verb -eq 'hover' -and $steps[1].X -eq 1800 -and $steps[1].Y -eq 660) 'x,y is a client offset'
    Check ($steps[3].Ms -eq 500 -and $steps[4].Name -eq 'after-escape' -and $steps[4].What -eq 'the panel closed') 'wait and screenshot keep their values'
    Check (Throws { ConvertFrom-GameRunSteps @('jump 1,2') (Merge-GameRunPoints) } "line 1.*unknown verb 'jump'") 'fail case: an unknown verb names its line'
    Check (Throws { ConvertFrom-GameRunSteps @('click @Nowhere') (Merge-GameRunPoints) } "no point named '@Nowhere'") 'fail case: an unknown @Name'
    Check (Throws { ConvertFrom-GameRunSteps @('click 2600,10') (Merge-GameRunPoints) } 'outside the 2560x1440') 'fail case: a point outside the client'
    Check (Throws { ConvertFrom-GameRunSteps @('key Space') (Merge-GameRunPoints) } 'toggles the pause') 'fail case: Space (it would unpause)'
    Check (Throws { ConvertFrom-GameRunSteps @('wait 20000') (Merge-GameRunPoints) } '0 to 10000') 'fail case: a wait over 10 s'
    Check (Throws { ConvertFrom-GameRunSteps @('screenshot Bad_Name') (Merge-GameRunPoints) } 'takes a name') 'fail case: a screenshot name with capitals or underscores'

    Write-Host '--- the paused check ---'
    $pausedPng = Join-Path $root 'paused.png'; $runningPng = Join-Path $root 'running.png'
    $b = New-Bitmap 2560 1440 ([System.Drawing.Color]::FromArgb(230, 235, 240))
    $g = [System.Drawing.Graphics]::FromImage($b); $g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(74, 192, 240)), 1170, 4, 220, 44); $g.Dispose()
    $b.Save($pausedPng, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    $b = New-Bitmap 2560 1440 ([System.Drawing.Color]::FromArgb(230, 235, 240)); $b.Save($runningPng, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    Check (Test-PausedBanner $pausedPng) 'the cyan PAUSED banner top centre reads as paused'
    Check (-not (Test-PausedBanner $runningPng)) 'fail case: snow-white map there reads as running'

    Write-Host '--- the deployed build ---'
    $mods = Join-Path $root 'Mods'
    New-Item -ItemType Directory -Force (Join-Path $mods 'FiscalOverhaul') | Out-Null
    Set-Content -LiteralPath (Join-Path $mods 'FiscalOverhaul\build-info.txt') -Value @('branch feature/x', 'commit abc1234', '')
    Check (((Get-DeployedBuildInfo $mods) -join '|') -eq 'branch feature/x|commit abc1234') 'build-info.txt''s lines name what is deployed'
    Check ((Get-DeployedBuildInfo (Join-Path $root 'no-mods')) -match 'unknown') 'fail case: no build-info.txt says unknown'

    # --- the script, against the stub ---
    $stubPath = Join-Path $root 'game-control-stub.ps1'
    Set-Content -LiteralPath $stubPath -Value $Stub
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') $fakeExe
    $env:FO_GAME_CONTROL = $stubPath
    $env:FO_STUB_LOG = $log
    $env:FO_GAME_PROCESS_NAME = $fakeName
    $env:FO_GAME_LAUNCH = $fakeExe
    $env:FO_GAME_LAUNCH_ARGS = '-n 600 127.0.0.1'
    $env:FO_GAME_DATA_DIR = Join-Path $root 'game-data'
    $out = Join-Path $root 'out'

    Write-Host '--- arguments ---'
    $r = Invoke-Check @()
    Check ($r.ExitCode -eq 2 -and $r.Output -match '-Out <folder> is required') 'fail case: no -Out is refused' $r.Output
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-Ref', 'origin/autodev')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'Give one') 'fail case: -Ref with -SkipDeploy is refused' $r.Output
    $stepsFile = Join-Path $root 'steps.txt'
    Set-Content -LiteralPath $stepsFile -Value @('click @BudgetTab', 'screenshot budget-again the Budget tab again')
    $r = Invoke-Check @('-Out', $out, '-Steps', $stepsFile, '-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -match '2 extra step' -and $r.Calls -eq '') 'a dry run checks the steps file and launches nothing' $r.Output
    Set-Content -LiteralPath $stepsFile -Value @('click @BudgetTab', 'press Escape')
    $r = Invoke-Check @('-Out', $out, '-Steps', $stepsFile, '-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "line 2.*unknown verb 'press'") 'fail case: a bad steps file is refused before anything runs' $r.Output
    Set-Content -LiteralPath $stepsFile -Value @('screenshot taxation overwrites a fixed shot')
    $r = Invoke-Check @('-Out', $out, '-Steps', $stepsFile, '-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'would overwrite one of the fixed shots') 'fail case: a step shot named like a fixed one is refused' $r.Output

    Write-Host '--- the claim ---'
    $preexisting = Start-Process -FilePath $fakeExe -ArgumentList '-n', '600', '127.0.0.1' -WindowStyle Hidden -PassThru
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-ModsPath', $mods)
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'Game: RUNNING') 'fail case: refused (exit 2) while a game runs' $r.Output
    Check ($r.Calls -eq '' -and -not (Test-Path -LiteralPath (Join-Path $out 'manifest.md'))) '... launching and driving nothing' $r.Calls
    Check (-not $preexisting.HasExited) '... and the instance it didn''t launch is left running'
    Stop-Process -Id $preexisting.Id -Force; $preexisting.WaitForExit(); $preexisting = $null

    Write-Host '--- a green run ---'
    Set-Content -LiteralPath $stepsFile -Value @('click @BudgetTab', 'screenshot budget-again the Budget tab again')
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-ModsPath', $mods, '-Steps', $stepsFile) @{ FO_STUB_EXIT_KILLS = '1' }
    Check ($r.ExitCode -eq 0) 'the run is green (exit 0)' $r.Output
    foreach ($name in 'loaded.png', 'budget.png', 'budget-hover.png', 'taxation.png', 'budget-again.png', 'quit-menu.png', 'manifest.md') {
        Check (Test-Path -LiteralPath (Join-Path $out $name)) "... writes $name"
    }
    Check ($r.Calls -match '(?m)^click 2050,1456$' -and $r.Calls -match '(?m)^hover 2145,701$' -and $r.Calls -match '(?m)^click 1380,356$') '... clicking the money total, hovering ChartPoint and clicking Taxation at origin + offset' $r.Calls
    Check ($r.Calls -match '(?m)^click 836,356$') '... then the steps file''s click (BudgetTab)' $r.Calls
    Check ($r.Calls -notmatch '(?m)^key Space$') '... never pressing Space on a paused save' $r.Calls
    Check ($r.Calls -match '(?ms)^key Escape$.*^key Escape$.*^click 918,1190$.*^click 1378,850$') '... quitting through Escape, Escape, Exit, Exit to Desktop' $r.Calls
    Check (-not (Test-FakeRunning) -and $r.Output -match 'Game: not running' -and $r.Output -notmatch 'Stopping the instance') '... which ends the game: Game: not running, nothing force-stopped' $r.Output
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $out 'manifest.md')
    Check ($manifest -match 'Result: green' -and $manifest -match 'branch feature/x, commit abc1234' -and $manifest -match '\| budget-again\.png \| the Budget tab again \|') '... and a manifest naming the result, the deployed build and every shot' $manifest

    Write-Host '--- the pause ---'
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-ModsPath', $mods) @{ FO_STUB_UNPAUSED = 'once' }
    $spaces = @($r.Calls -split "`n" | Where-Object { $_ -eq 'key Space' }).Count
    Check ($r.ExitCode -eq 0 -and $spaces -eq 1) 'a running save gets exactly one Space, then the run goes on' "$spaces Space(s)`n$($r.Output)"
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-ModsPath', $mods) @{ FO_STUB_UNPAUSED = 'never' }
    $spaces = @($r.Calls -split "`n" | Where-Object { $_ -eq 'key Space' }).Count
    Check ($r.ExitCode -eq 1 -and $spaces -eq 1 -and $r.Output -match "isn't paused after one Space") 'fail case: still running after one Space is red, without a second Space' "$spaces Space(s)`n$($r.Output)"
    Check (-not (Test-FakeRunning) -and $r.Output -match 'Game: not running') '... and the launched game is still stopped' $r.Output

    Write-Host '--- a red run ---'
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-ModsPath', $mods) @{ FO_STUB_FAIL_AT = 'click 1380,356' }
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'failed: game-control click') 'a failing step makes the run red (exit 1)' $r.Output
    Check (-not (Test-FakeRunning) -and $r.Output -match 'Stopping the instance this run launched' -and $r.Output -match 'Game: not running') '... and the finally still stops the instance it launched: Game: not running' $r.Output
    Check ($r.Calls -notmatch '(?m)^key Escape$') '... without menu keys in an unknown state' $r.Calls
    Check ((Get-Content -Raw -LiteralPath (Join-Path $out 'manifest.md')) -match 'Result: red - game-control click') '... and the manifest says where it failed'
    $r = Invoke-Check @('-Out', $out, '-SkipDeploy', '-ModsPath', $mods) @{ FO_STUB_FAIL_AT = 'wait-loaded' }
    Check ($r.ExitCode -eq 1 -and -not (Test-FakeRunning) -and $r.Output -match 'Game: not running') 'a load that never finishes is red and the launched game is stopped' $r.Output
}
finally {
    if ($preexisting) { Stop-Process -Id $preexisting.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name $fakeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($name in $savedNames) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 300
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All in-game-check.ps1 checks passed.' -ForegroundColor Green
exit 0
