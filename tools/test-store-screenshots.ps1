<#
.SYNOPSIS
    Tests tools/store-screenshots.ps1 (and its parts of tools/game-run-common.ps1) without the game.

.DESCRIPTION
    Never launches Steam or the game, never touches this repo or the real settings file: a fixture checkout under TEMP
    (a git repo with a v1.1.0 tag, PublishConfiguration.xml, a README and the old screenshots) is -RepoRoot;
    FO_GAME_DATA_DIR points at a fixture FiscalOverhaul.coc; FO_GAME_CONTROL names a stub game-control.ps1 that logs
    each command and paints 2560x1440 screenshots (the PAUSED banner; the Budget check squares filled, or not), and
    FO_GAME_LAUNCH starts a renamed ping.exe as the "game". What must hold, each with a pass and a fail case: the
    version the names carry (ModVersion, the next patch after a released tag, -Version), the suffixed names and their
    base, the config and README rewrites (order kept, other entries untouched), the settings rewrite, the crop box and
    scale; the argument refusals; -FromDir installs the whole set or nothing; the refusal while a game runs; a green run
    turns the rows on, takes the nine shots, installs them and puts the settings file back byte for byte; a red run
    (rows that stay off) installs nothing, puts the settings back and stops the game it launched; a killed run's
    settings copy is put back first.
        ./tools/test-store-screenshots.ps1     (exit 0 = all passed)
#>
#Requires -Version 7.0
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
function Write-File([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Git([string]$Path, [string[]]$Arguments) { (Invoke-Git -Path $Path -Arguments $Arguments).Text }

$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
$root = Join-Path ([IO.Path]::GetTempPath()) "fo-test-store-screenshots-$id"
$repo = Join-Path $root 'repo'
$log = Join-Path $root 'calls.txt'
$shell = (Get-Process -Id $PID).Path
$fakeName = "fo-fake-game-$id"
$fakeExe = Join-Path $root "$fakeName.exe"
$gameData = Join-Path $root 'game-data'
$settings = Join-Path $gameData 'FiscalOverhaul.coc'
$savedNames = 'FO_GAME_CONTROL', 'FO_GAME_LAUNCH', 'FO_GAME_LAUNCH_ARGS', 'FO_GAME_PROCESS_NAME', 'FO_GAME_DATA_DIR', 'FO_STUB_LOG',
    'FO_STUB_FAIL_AT', 'FO_STUB_UNPAUSED', 'FO_STUB_ROWS_OFF', 'FO_STUB_SEEN', 'FO_STUB_SPACE_IGNORED'

# The page order on purpose differs from the file numbers (as the real config's does).
$OldOrder = '01-budget-chart', '06-budget-district', '07-budget-district-list', '02-budget-tooltip', '05-budget-vanilla-detail', '03-taxation-density', '04-options'
$FixtureConfig = @"
<Publish>
	<ModId Value="4242" />
	<Thumbnail Value="Properties/Thumbnail.png" />
	<!--Screenshot, can be set multiple times-->
$(($OldOrder | ForEach-Object { "`t<Screenshot Value=`"Properties/Screenshots/$_.png`" />" }) -join "`n")
	<Screenshot Value="Properties/Screenshots/99-extra.png" />
	<ModVersion Value="1.1.0" />
</Publish>
"@
$FixtureReadme = @"
# Fiscal Overhaul

![The chart](Mod/Properties/Screenshots/01-budget-chart.png)
Text about 01-budget-chart.png stays as it is.
![Options](Mod/Properties/Screenshots/04-options.png)
![Extra](Mod/Properties/Screenshots/99-extra.png)
"@
$FixtureSettings = "FiscalOverhaul`n{`n    `"DensityTaxOffsets`": `"0,0,0,0,0,0,0,0,0,0,0,0`",`n    `"EnableDensityTaxes`": false,`n    `"SomethingElse`": 7`n}`n"

$Stub = @'
param([Parameter(Position = 0)][string]$Command, [Parameter(Position = 1)][string]$Name, [string]$Out, [double]$Scale = 1,
    [switch]$Window, [string]$Title, [int]$X, [int]$Y, [switch]$Right, [switch]$Double, [int]$Delay = 0, [int]$Timeout = 300, [switch]$Gone)
$line = $Command
if ($Name) { $line += " $Name" }
if ($PSBoundParameters.ContainsKey('X')) { $line += " $X,$Y" }
if ($Gone) { $line += ' -Gone' }
if ($Out) { $line += " $([IO.Path]::GetFileName($Out)) scale $Scale" }
Add-Content -LiteralPath $env:FO_STUB_LOG -Value $line
if ($env:FO_STUB_FAIL_AT -and $line -like $env:FO_STUB_FAIL_AT) { [Console]::Error.WriteLine("stub: failing '$line'"); exit 1 }
# Space toggles the pause (unless FO_STUB_SPACE_IGNORED); the PAUSED banner is painted only while paused.
$running = "$env:FO_STUB_LOG.running"
switch ($Command) {
    'key' {
        if ($Name -eq 'Space' -and -not $env:FO_STUB_SPACE_IGNORED) {
            if (Test-Path -LiteralPath $running) { Remove-Item -LiteralPath $running } else { Set-Content -LiteralPath $running -Value '' }
        }
        'key ok'
    }
    'window' { 'client: 100,50,2560,1440' }
    'screenshot' {
        Add-Type -AssemblyName System.Drawing
        $s = $Scale
        $b = [System.Drawing.Bitmap]::new([int](2560 * $s), [int](1440 * $s))
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.Clear([System.Drawing.Color]::FromArgb(60, 73, 91))
        if (-not (Test-Path -LiteralPath $running)) { $g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(74, 192, 240)), [int](1170 * $s), [int](4 * $s), [int](220 * $s), [int](44 * $s)) }
        if (-not $env:FO_STUB_ROWS_OFF) { $g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(113, 241, 168)), [int](370 * $s), [int](380 * $s), [int](70 * $s), [int](700 * $s)) }
        $g.Dispose()
        $b.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Dispose()
        "screenshot: $Out"
    }
    'wait-loaded' {
        Copy-Item -LiteralPath (Join-Path $env:FO_GAME_DATA_DIR 'FiscalOverhaul.coc') -Destination $env:FO_STUB_SEEN -Force
        'wait-loaded: loaded'
    }
    'wait-process' {
        if ($Gone) { if (Get-Process -Name $env:FO_GAME_PROCESS_NAME -ErrorAction SilentlyContinue) { [Console]::Error.WriteLine('stub: still running'); exit 1 } }
        'wait-process: ok'
    }
    default { "$Command ok" }
}
'@

function New-Png([string]$Path, [int]$W, [int]$H, [scriptblock]$Paint = $null) {
    $b = [System.Drawing.Bitmap]::new($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.Clear([System.Drawing.Color]::FromArgb(20, 30, 40))
    if ($Paint) { & $Paint $g }
    $g.Dispose()
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
    $b.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
function Get-ImageInfo([string]$Path) {
    $b = [System.Drawing.Bitmap]::FromFile($Path)
    try { return [pscustomobject]@{ Width = $b.Width; Height = $b.Height; Format = $b.PixelFormat; Bitmap = $null; Centre = $b.GetPixel([int]($b.Width / 2), [int]($b.Height / 2)); Corner = $b.GetPixel(10, 10) } }
    finally { $b.Dispose() }
}

# Resets the fixture checkout: config, README, the old screenshots (and an unrelated one), committed and tagged v1.1.0.
function Reset-Fixture {
    if (Test-Path -LiteralPath $repo) { Remove-Item -LiteralPath $repo -Recurse -Force }
    New-Item -ItemType Directory -Force $repo | Out-Null
    Git $repo @('init', '-q', '-b', 'master') | Out-Null
    Git $repo @('config', 'user.email', 'test@example.com') | Out-Null
    Git $repo @('config', 'user.name', 'Test') | Out-Null
    Git $repo @('config', 'core.hooksPath', (Join-Path $root 'no-hooks')) | Out-Null
    # With a byte-order mark, as the real config has one; the README without.
    New-Item -ItemType Directory -Force (Join-Path $repo 'Mod\Properties') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'Mod\Properties\PublishConfiguration.xml'), $FixtureConfig, (New-Object System.Text.UTF8Encoding($true)))
    Write-File (Join-Path $repo 'README.md') $FixtureReadme
    foreach ($base in $OldOrder + '99-extra') { Write-File (Join-Path $repo "Mod\Properties\Screenshots\$base.png") 'old' }
    Git $repo @('add', '-A') | Out-Null
    Git $repo @('commit', '-q', '-m', 'fixture') | Out-Null
    Git $repo @('tag', 'v1.1.0') | Out-Null
}
function Get-RepoState {
    $files = (Get-ChildItem -LiteralPath (Join-Path $repo 'Mod\Properties\Screenshots') -File | ForEach-Object { $_.Name }) -join ','
    return $files + '|' + [IO.File]::ReadAllText((Join-Path $repo 'Mod\Properties\PublishConfiguration.xml')) + '|' + [IO.File]::ReadAllText((Join-Path $repo 'README.md'))
}

function Invoke-Store([string[]]$Arguments, [hashtable]$Env = @{}) {
    Set-Content -LiteralPath $log -Value $null
    Remove-Item -LiteralPath "$log.running" -ErrorAction SilentlyContinue
    foreach ($name in 'FO_STUB_FAIL_AT', 'FO_STUB_ROWS_OFF', 'FO_STUB_SPACE_IGNORED') { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    # A one-second simulation run unless the test sets its own.
    if ($Arguments -notcontains '-SimSeconds') { $Arguments += @('-SimSeconds', '1') }
    foreach ($key in $Env.Keys) { Set-Item "Env:$key" $Env[$key] }
    $global:LASTEXITCODE = 0
    $ErrorActionPreference = 'Continue'
    try { $output = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'store-screenshots.ps1') -RepoRoot $repo @Arguments 2>&1 | Out-String }
    finally { $ErrorActionPreference = 'Stop' }
    $calls = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log) -join "`n" } else { '' }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Calls = $calls }
}
function Test-FakeRunning { return [bool](Get-Process -Name $fakeName -ErrorAction SilentlyContinue) }

$preexisting = $null
try {
    New-Item -ItemType Directory -Force $root, $gameData | Out-Null
    Add-Type -AssemblyName System.Drawing

    Write-Host '--- the version and the names ---'
    Check ((Get-ScreenshotVersion '1.1.0' @('v1.0.0', 'v1.1.0')) -eq '1.1.1') 'a released ModVersion (v1.1.0 tagged) names the next patch, 1.1.1'
    Check ((Get-ScreenshotVersion '1.2.0' @('v1.0.0', 'v1.1.0')) -eq '1.2.0') 'an unreleased ModVersion (1.2.0) is used as it is'
    Check ((Get-ScreenshotVersion '1.1.0' @('v1.1.0', 'v1.10.0', 'v1.9.0')) -eq '1.10.1') '... comparing tags as versions, not text (v1.10.0 > v1.9.0)'
    Check ((Get-ScreenshotVersion '1.1.0' @('v1.1.0') '2.0.0') -eq '2.0.0') '-Version overrides both'
    Check (Throws { Get-ScreenshotVersion '1.1.0' @() 'two' } "isn't X.Y.Z") 'fail case: a -Version that isn''t X.Y.Z'
    Check (Throws { Get-ScreenshotVersion '' @() } 'give -Version') 'fail case: no ModVersion and no -Version'
    Check ((Get-StoreFileName '01-budget-chart' '1.2.0') -eq '01-budget-chart-v1-2-0.png') 'the name is <base>-v<X-Y-Z>.png'
    Check ((Get-StoreBase '01-budget-chart-v1-2-0.png') -eq '01-budget-chart' -and (Get-StoreBase '01-budget-chart.png') -eq '01-budget-chart') 'the base drops .png and any version suffix'
    Check ((Get-StoreBase '01-budget-chart-v2.png') -eq '01-budget-chart-v2') 'fail case: only a full -vX-Y-Z suffix is dropped'

    Write-Host '--- the rewrites ---'
    $names = @{}
    foreach ($shot in $StoreShotSet) { $names[$shot.Base] = Get-StoreFileName $shot.Base '1.1.1' }
    $newConfig = Update-StoreConfigText $FixtureConfig $names
    $listed = @([regex]::Matches($newConfig, 'Screenshots/([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $expected = @($OldOrder | ForEach-Object { "$_-v1-1-1.png" }) + '99-extra.png'
    Check (($listed -join ',') -eq ($expected -join ',')) 'the config''s Screenshot entries are renamed in their page order' ($listed -join "`n")
    Check ($newConfig -match '<Thumbnail Value="Properties/Thumbnail.png" />' -and $newConfig -match '99-extra\.png') '... the Thumbnail and an unknown screenshot untouched'
    Check ((Update-StoreConfigText $newConfig $names) -eq $newConfig) '... and a second run changes nothing (already-suffixed names are recognised)'
    $newReadme = Update-StoreReadmeText $FixtureReadme $names
    Check ($newReadme -match '\(Mod/Properties/Screenshots/01-budget-chart-v1-1-1\.png\)' -and $newReadme -match '04-options-v1-1-1\.png') 'the README links are renamed'
    Check ($newReadme -match 'Text about 01-budget-chart\.png stays' -and $newReadme -match '99-extra\.png\)') 'fail case: text outside a screenshot path and unknown files are left alone'
    $newSettings = Set-StoreSettingsText $FixtureSettings '2,1,-1,0,0,0,0,0,0,0,0,0'
    $parsed = $newSettings.Substring($newSettings.IndexOf('{')) | ConvertFrom-Json
    Check ($newSettings.StartsWith("FiscalOverhaul`n{") -and $parsed.EnableDensityTaxes -eq $true -and $parsed.EnableBudgetChart -eq $true -and $parsed.EnableDistrictBudget -eq $true -and $parsed.DensityTaxOffsets -eq '2,1,-1,0,0,0,0,0,0,0,0,0') 'the settings file gets the features on and the offsets' $newSettings
    Check ($parsed.SomethingElse -eq 7) '... keeping its other keys' $newSettings
    Check ($parsed.EnableServicesTable -eq $true -and $parsed.EnableSimulationChanges -eq $true) '... the services table and simulation changes on too (08-services-table, 09-taxation-district)' $newSettings

    Write-Host '--- the Budget rows'' check squares ---'
    # Each row is read at its own square: the totals at x 389, the rows under them at 418.
    $points = Merge-GameRunPoints @()
    $squares = Join-Path $root 'rows/squares.png'
    $green = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(113, 241, 168))
    New-Png $squares 2560 1440 { param($g) foreach ($row in $GameRunBudgetRows) { $g.FillRectangle($green, $points[$row].X - 5, $points[$row].Y - 5, 10, 10) } }
    Check ((Get-UncheckedRows $squares $points).Count -eq 0) 'every square filled at its own x: no row reads off'
    $totalsOnly = Join-Path $root 'rows/totals-only.png'
    New-Png $totalsOnly 2560 1440 { param($g) $g.FillRectangle($green, 384, 380, 10, 700) }
    $off = Get-UncheckedRows $totalsOnly $points
    Check ($off -contains 'RowTaxes' -and $off -notcontains 'RowRevenues' -and $off -notcontains 'RowBalance') 'fail case: only the totals'' column filled: the indented rows read off' ($off -join ', ')
    Check ((Set-StoreSettingsText '' '0,0,0,0,0,0,0,0,0,0,0,1') -match '"DensityTaxOffsets": "0,0,0,0,0,0,0,0,0,0,0,1"') 'no settings file yet: one is written'
    Check (Throws { Set-StoreSettingsText $FixtureSettings '1,2,3' } '12 comma-separated') 'fail case: offsets that aren''t 12 integers'

    Write-Host '--- the crop ---'
    $raw = Join-Path $root 'crop\raw.png'
    # A red block at the centre of the box and a white one outside it (top left).
    New-Png $raw 2560 1440 { param($g) $g.FillRectangle([System.Drawing.Brushes]::Red, 1230, 702, 100, 100); $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 200, 150) }
    $cropped = Join-Path $root 'crop\panel.png'
    Save-CroppedImage $raw $cropped $StoreCropBox $StoreImageWidth $StoreImageHeight
    $info = Get-ImageInfo $cropped
    Check ($info.Width -eq 1920 -and $info.Height -eq 1080) 'a panel shot comes out 1920x1080'
    Check ($info.Format -eq [System.Drawing.Imaging.PixelFormat]::Format24bppRgb) '... as a 24-bit RGB PNG (under the 2.1 MB page-image limit)' "$($info.Format)"
    Check ($info.Format -ne [System.Drawing.Imaging.PixelFormat]::Format32bppArgb) 'fail case: not the default 32-bit ARGB, a third bigger for an opaque shot' "$($info.Format)"
    Check ($info.Centre.R -gt 200 -and $info.Centre.G -lt 60) '... centred on the box''s centre (the red block)'
    Check ($info.Corner.R -lt 60) 'fail case: what lies outside the box (the white corner) is cut off'
    $whole = Join-Path $root 'crop\options.png'
    Save-CroppedImage $raw $whole $null $StoreImageWidth $StoreImageHeight
    $info = Get-ImageInfo $whole
    Check ($info.Width -eq 1920 -and $info.Height -eq 1080 -and $info.Corner.R -gt 200) 'the Options shot is scaled whole (the white corner stays)'
    Check (Throws { Save-CroppedImage (Join-Path $root 'crop\panel.png') (Join-Path $root 'crop\x.png') $StoreCropBox 1920 1080 } "doesn't fit") 'fail case: a capture smaller than the box is refused'

    # --- the script ---
    $stubPath = Join-Path $root 'game-control-stub.ps1'
    Set-Content -LiteralPath $stubPath -Value $Stub
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') $fakeExe
    $env:FO_GAME_CONTROL = $stubPath
    $env:FO_STUB_LOG = $log
    $env:FO_STUB_SEEN = Join-Path $root 'seen-settings.coc'
    $env:FO_GAME_PROCESS_NAME = $fakeName
    $env:FO_GAME_LAUNCH = $fakeExe
    $env:FO_GAME_LAUNCH_ARGS = '-n 600 127.0.0.1'
    $env:FO_GAME_DATA_DIR = $gameData
    $mods = Join-Path $root 'Mods'
    Write-File (Join-Path $mods 'FiscalOverhaul\build-info.txt') "branch autodev`ncommit def5678`n"
    $checkDir = Join-Path $root 'check'

    Write-Host '--- arguments ---'
    Reset-Fixture
    $r = Invoke-Store @('-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'version 1\.1\.1 \(ModVersion 1\.1\.0 is tagged' -and $r.Output -match '01-budget-chart-v1-1-1\.png') 'a dry run names version 1.1.1 (v1.1.0 is released) and the new names' $r.Output
    $r = Invoke-Store @('-DryRun', '-Version', '1.2.0')
    Check ($r.ExitCode -eq 0 -and $r.Output -match '04-options-v1-2-0\.png') '-Version 1.2.0 names -v1-2-0' $r.Output
    $r = Invoke-Store @('-DryRun', '-Offsets', '1,2')
    Check ($r.ExitCode -eq 2 -and $r.Output -match '12 comma-separated') 'fail case: bad -Offsets are refused' $r.Output
    $r = Invoke-Store @('-FromDir', $root, '-SkipDeploy')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'mean nothing') 'fail case: -FromDir with -SkipDeploy is refused' $r.Output

    Write-Host '--- -FromDir ---'
    $rawDir = Join-Path $root 'raw'
    foreach ($shot in $StoreShotSet) { New-Png (Join-Path $rawDir "$($shot.Base).png") 2560 1440 }
    Remove-Item -LiteralPath (Join-Path $rawDir '04-options.png')
    $before = Get-RepoState
    $r = Invoke-Store @('-FromDir', $rawDir, '-CheckDir', $checkDir)
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'lacks 04-options\.png' -and (Get-RepoState) -eq $before) 'fail case: a missing capture installs nothing' $r.Output
    New-Png (Join-Path $rawDir '04-options.png') 2560 1440
    $r = Invoke-Store @('-FromDir', $rawDir, '-CheckDir', $checkDir)
    Check ($r.ExitCode -eq 0) 'all the captures install (exit 0)' $r.Output
    $files = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Mod\Properties\Screenshots') -File | ForEach-Object { $_.Name } | Sort-Object)
    $want = @(($StoreShotSet | ForEach-Object { "$($_.Base)-v1-1-1.png" }) + '99-extra.png' | Sort-Object)
    Check (($files -join ',') -eq ($want -join ',')) '... the new names in Screenshots/, the old files gone, an unrelated one kept' ($files -join "`n")
    $info = Get-ImageInfo (Join-Path $repo 'Mod\Properties\Screenshots\01-budget-chart-v1-1-1.png')
    Check ($info.Width -eq 1920 -and $info.Height -eq 1080) '... each 1920x1080'
    $config = [IO.File]::ReadAllText((Join-Path $repo 'Mod\Properties\PublishConfiguration.xml'))
    $listed = @([regex]::Matches($config, 'Screenshots/([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    Check (($listed -join ',') -eq ($expected -join ',')) '... the config renamed in page order' ($listed -join "`n")
    Check ([IO.File]::ReadAllText((Join-Path $repo 'README.md')) -match '01-budget-chart-v1-1-1\.png') '... and the README'
    $configBytes = [IO.File]::ReadAllBytes((Join-Path $repo 'Mod\Properties\PublishConfiguration.xml'))
    $readmeBytes = [IO.File]::ReadAllBytes((Join-Path $repo 'README.md'))
    Check ($configBytes[0] -eq 0xEF -and $configBytes[1] -eq 0xBB -and $configBytes[2] -eq 0xBF -and $configBytes[3] -eq [byte][char]'<') '... the config keeping its byte-order mark (once, not doubled)'
    Check ($readmeBytes[0] -eq [byte][char]'#') 'fail case: the README, which had none, gets none'
    Check ($r.Calls -eq '') '... without touching the game' $r.Calls

    Write-Host '--- the claim ---'
    Reset-Fixture
    Write-File $settings $FixtureSettings
    $before = Get-RepoState
    $preexisting = Start-Process -FilePath $fakeExe -ArgumentList '-n', '600', '127.0.0.1' -WindowStyle Hidden -PassThru
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir)
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'Game: RUNNING') 'fail case: refused (exit 2) while a game runs' $r.Output
    Check ($r.Calls -eq '' -and [IO.File]::ReadAllText($settings) -eq $FixtureSettings -and (Get-RepoState) -eq $before) '... driving nothing, the settings file and the checkout untouched' $r.Calls
    Check (-not $preexisting.HasExited) '... and that instance left running'
    Stop-Process -Id $preexisting.Id -Force; $preexisting.WaitForExit(); $preexisting = $null

    Write-Host '--- a green run ---'
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir)
    Check ($r.ExitCode -eq 0) 'the run is green (exit 0)' $r.Output
    $seen = [IO.File]::ReadAllText($env:FO_STUB_SEEN)
    Check ($seen -match '"EnableDensityTaxes": true' -and $seen -match '"DensityTaxOffsets": "2,1,-1,0,0,0,0,0,0,0,0,0"') '... the game loaded with density taxes on and the default offsets' $seen
    Check ([IO.File]::ReadAllText($settings) -eq $FixtureSettings -and -not (Test-Path -LiteralPath "$settings.store-screenshots-backup")) '... and the settings file is back byte for byte afterwards'
    Check ($r.Output -match 'rows off at first: none' -and $r.Calls -notmatch '(?m)^click (489|518),') '... rows already on are not clicked (a click would turn one off)' $r.Calls
    Check ($r.Calls -match '(?m)^click 864,356$' -and $r.Calls -match '(?m)^click 668,356$' -and $r.Calls -match '(?m)^click 600,620$' -and $r.Calls -match '(?m)^click 1596,356$' -and $r.Calls -match '(?m)^click 918,1092$') '... the header picker''s arrow and name, the Density section, the Services tab and Options at origin + offset' $r.Calls
    Check ($r.Calls -match '(?m)^click 1696,789$' -and $r.Calls -notmatch '(?m)^click 1696,446$') '... density taxes switched back off on the Options page (its own box, not Record district budgets)' $r.Calls
    $spaces = @($r.Calls -split "`n" | Where-Object { $_ -eq 'key Space' }).Count
    Check ($spaces -eq 2 -and $r.Calls -match '(?s)key Space\n.*key Space\n(?:(?!click )[^\n]*\n)*click 1324,356') '... the simulation run: two Spaces (start, pause again) just before the Taxation tab' $r.Calls
    $files = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Mod\Properties\Screenshots') -File | ForEach-Object { $_.Name } | Sort-Object)
    Check (($files -join ',') -eq ($want -join ',')) '... the set installed under the new names, the old ones removed' ($files -join "`n")
    Check (-not (Test-FakeRunning) -and $r.Output -match 'Game: not running') '... and the launched game stopped: Game: not running' $r.Output
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $checkDir 'manifest.md')
    Check ($manifest -match 'Result: green' -and $manifest -match 'Version: 1\.1\.1' -and $manifest -match 'rows-on\.png' -and $manifest -match 'options-open\.png') '... with a manifest of the shots and the check shots' $manifest
    Check ($manifest -match 'sim-running\.png' -and $manifest -match 'sim-paused\.png') '... the simulation run''s two check shots in it' $manifest

    Write-Host '--- the simulation run ---'
    Reset-Fixture
    $before = Get-RepoState
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir) @{ FO_STUB_SPACE_IGNORED = '1' }
    Check ($r.ExitCode -eq 1 -and $r.Output -match "Space didn't start the simulation") 'fail case: a Space that leaves the PAUSED banner makes the run red' $r.Output
    Check ((Get-RepoState) -eq $before -and [IO.File]::ReadAllText($settings) -eq $FixtureSettings) '... installing nothing, the settings file put back'
    Check (-not (Test-FakeRunning) -and $r.Output -match 'Game: not running') '... and the launched game stopped' $r.Output
    Reset-Fixture
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir, '-SimSeconds', '0')
    Check ($r.ExitCode -eq 0 -and $r.Calls -notmatch '(?m)^key Space$') '-SimSeconds 0: no run, no Space on the paused save' $r.Calls

    Write-Host '--- rows that stay off ---'
    Reset-Fixture
    $before = Get-RepoState
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir) @{ FO_STUB_ROWS_OFF = '1' }
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'rows still off after clicking them: RowRevenues') 'fail case: rows that read off after the clicks make the run red' $r.Output
    Check ($r.Calls -match '(?m)^click 489,445$' -and $r.Calls -match '(?m)^click 489,1022$' -and $r.Calls -match '(?m)^click 518,500$') '... after clicking every row that read off (Revenues to Monthly Balance)' $r.Calls
    Check ((Get-RepoState) -eq $before) '... installing nothing'
    Check ([IO.File]::ReadAllText($settings) -eq $FixtureSettings) '... putting the settings file back'
    Check (-not (Test-FakeRunning) -and $r.Output -match 'Game: not running') '... and stopping the game it launched' $r.Output

    Write-Host '--- a killed run''s copy ---'
    Write-File $settings 'left by a killed run'
    Write-File "$settings.store-screenshots-backup" $FixtureSettings
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir) @{ FO_STUB_FAIL_AT = 'wait-loaded' }
    Check ($r.Output -match "restored .* from a previous run's copy" -and [IO.File]::ReadAllText($settings) -eq $FixtureSettings) 'a backup left by a killed run is put back first, and stays the file after the run' $r.Output
    Check ($r.ExitCode -eq 1 -and -not (Test-FakeRunning)) '... (a load that never finishes is red, and the game is stopped)' $r.Output
    Remove-Item -LiteralPath $settings
    $r = Invoke-Store @('-SkipDeploy', '-ModsPath', $mods, '-CheckDir', $checkDir) @{ FO_STUB_FAIL_AT = 'wait-loaded' }
    Check (-not (Test-Path -LiteralPath $settings) -and -not (Test-Path -LiteralPath "$settings.store-screenshots-absent")) 'no settings file before: none after, red or green' $r.Output
}
finally {
    if ($preexisting) { Stop-Process -Id $preexisting.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name $fakeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($name in $savedNames) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 300
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All store-screenshots.ps1 checks passed.' -ForegroundColor Green
exit 0
