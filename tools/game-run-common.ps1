<#
    Shared launch, drive and quit code for the scripted game runs (in-game-check.ps1, store-screenshots.ps1).
    Dot-source it after git-common.ps1; it only defines functions and data. Everything the game shows is driven through
    tools/game-control.ps1 (knowledge/testing.md#on-windows-without-computer-use); the pure parts (the points, the
    crop box, the steps grammar, the pixel tests on screenshots) are what tools/test-in-game-check.ps1 and
    tools/test-store-screenshots.ps1 cover without the game.

    Testing hooks (never set in a real run): FO_GAME_CONTROL names a stand-in for game-control.ps1; FO_GAME_LAUNCH
    (+ FO_GAME_LAUNCH_ARGS) a program started instead of steam://rungameid/949230, after which the launcher steps are
    skipped unless FO_GAME_LAUNCH_VIA_LAUNCHER is set; FO_GAME_PROCESS_NAME the game's process name (git-common.ps1's Get-GameProcess); FO_GAME_DATA_DIR the
    game's LocalLow folder (where FiscalOverhaul.coc lives).
#>
#Requires -Version 7.0

$GameRunSteamUri = 'steam://rungameid/949230'
$GameRunLauncherTitle = '^Paradox Launcher$'
# Every offset below is a client offset in a 2560x1440 game window (knowledge/testing.md#on-windows-without-computer-use).
$GameRunClientWidth = 2560
$GameRunClientHeight = 1440

# The named points, as client offsets of the 2560x1440 game window (the launcher's Resume: of the launcher's client).
# Source says where each came from: "measured" = clicked in a real run; "derived" = read off a reference screenshot
# (the 1920x1080 store set, or a half-scale run shot) and converted, not yet clicked by a script: each script takes a
# check screenshot after clicking one, so a run can correct it with -Point Name=x,y.
$GameRunPoints = [ordered]@{
    # The launcher's size varies (1282x677 in one run, about 1.5x that in another), so this one scales with its client
    # area from the size it was measured at (RefWidth x RefHeight); see Get-LauncherResumePoint.
    LauncherResume    = @{ X = 660;  Y = 246;  RefWidth = 1282; RefHeight = 677; Source = 'measured'; What = "the Paradox launcher's Resume (launcher client, scaled to its size)" }
    MoneyTotal        = @{ X = 1950; Y = 1406; Source = 'measured'; What = 'the money total in the bottom bar: opens the Economy panel on Budget' }
    BudgetTab         = @{ X = 736;  Y = 306;  Source = 'measured'; What = 'the Economy panel''s Budget tab' }
    TaxationTab       = @{ X = 1280; Y = 306;  Source = 'measured'; What = 'the Economy panel''s Taxation tab' }
    ChartPoint        = @{ X = 2045; Y = 651;  Source = 'derived';  What = 'a point on the Revenues line, 17 h ago in the Odiham save (02-budget-tooltip)' }
    PauseExit         = @{ X = 818;  Y = 1140; Source = 'measured'; What = 'the pause menu''s Exit' }
    ExitToDesktop     = @{ X = 1278; Y = 800;  Source = 'measured'; What = 'Exit to Desktop in the Exit dialog' }
    PauseOptions      = @{ X = 818;  Y = 1042; Source = 'derived';  What = 'the pause menu''s Options (half-scale menu.png 430,521)' }
    Neutral           = @{ X = 627;  Y = 1177; Source = 'derived';  What = 'an empty spot in the Economy panel below the Budget rows: no hover' }
    RowCheckX         = @{ X = 389;  Y = 0;    Source = 'derived';  What = 'the x of the Budget rows'' check squares (Y unused)' }
    RowRevenues       = @{ X = 440;  Y = 453;  Source = 'derived';  What = 'Budget row: Revenues (on by default)' }
    RowTaxes          = @{ X = 440;  Y = 508;  Source = 'derived';  What = 'Budget row: Taxes; hovered for the detail pop-out' }
    RowServiceFees    = @{ X = 440;  Y = 563;  Source = 'derived';  What = 'Budget row: Service Fees' }
    RowIncomeTrade    = @{ X = 440;  Y = 618;  Source = 'derived';  What = 'Budget row: Service Trade (revenue)' }
    RowExpenses       = @{ X = 440;  Y = 685;  Source = 'derived';  What = 'Budget row: Expenses (on by default)' }
    RowSubsidies      = @{ X = 440;  Y = 741;  Source = 'derived';  What = 'Budget row: Subsidies' }
    RowUpkeeps        = @{ X = 440;  Y = 796;  Source = 'derived';  What = 'Budget row: Service Upkeeps' }
    RowExpenseTrade   = @{ X = 440;  Y = 851;  Source = 'derived';  What = 'Budget row: Service Trade (expense)' }
    RowLoanInterest   = @{ X = 440;  Y = 906;  Source = 'derived';  What = 'Budget row: Loan Interest' }
    RowTileUpkeep     = @{ X = 440;  Y = 961;  Source = 'derived';  What = 'Budget row: Tile Upkeep' }
    RowBalance        = @{ X = 440;  Y = 1028; Source = 'derived';  What = 'Budget row: Monthly Balance' }
    DistrictPrev      = @{ X = 1037; Y = 391;  Source = 'derived';  What = 'the district selector''s left arrow' }
    DistrictNext      = @{ X = 1528; Y = 391;  Source = 'derived';  What = 'the district selector''s right arrow' }
    DistrictName      = @{ X = 1282; Y = 391;  Source = 'derived';  What = 'the district selector''s name box: opens and closes the list' }
    ResidentialDensity = @{ X = 529; Y = 518;  Source = 'derived';  What = 'Taxation tab: Residential''s "By density" toggle' }
    OptionsModEntry   = @{ X = 283;  Y = 1048; Source = 'derived';  What = 'Options: the FISCAL OVERHAUL entry in the left list (moves with the installed mods)' }
    OptionsDensity    = @{ X = 1596; Y = 396;  Source = 'derived';  What = 'Options > Fiscal Overhaul: the Enable density taxes (BETA) check box' }
    OptionsNeutral    = @{ X = 1900; Y = 1200; Source = 'derived';  What = 'an empty spot on the Options page: no hover' }
}

# The Budget rows, top to bottom, that "all lines on" turns on.
$GameRunBudgetRows = @('RowRevenues', 'RowTaxes', 'RowServiceFees', 'RowIncomeTrade', 'RowExpenses', 'RowSubsidies',
    'RowUpkeeps', 'RowExpenseTrade', 'RowLoanInterest', 'RowTileUpkeep', 'RowBalance')

# The store set's 16:9 box around the Economy panel (client pixels, right/bottom exclusive) and the output size.
$StoreCropBox = @{ Left = 285; Top = 193; Right = 2275; Bottom = 1312 }
$StoreImageWidth = 1920
$StoreImageHeight = 1080

# A copy of the points table with -Point overrides applied: each "Name=x,y" (client offsets). Throws on an unknown
# name or a malformed value, naming the fix.
function Merge-GameRunPoints([string[]]$Overrides = @()) {
    $points = [ordered]@{}
    foreach ($key in $GameRunPoints.Keys) { $points[$key] = @{} + $GameRunPoints[$key] }
    foreach ($override in @($Overrides | Where-Object { $_ })) {
        if ($override -notmatch '^\s*([A-Za-z]+)\s*=\s*(-?\d+)\s*,\s*(-?\d+)\s*$') {
            throw "-Point '$override' isn't Name=x,y (client offsets in the 2560x1440 window), e.g. PauseOptions=818,1042"
        }
        $name = $Matches[1]
        $match = @($points.Keys | Where-Object { $_ -eq $name })
        if (-not $match) { throw "-Point '$override': no point named '$name'; the names: $($points.Keys -join ', ')" }
        $points[$match[0]] = @{ X = [int]$Matches[2]; Y = [int]$Matches[3]; Source = 'override'; What = $points[$match[0]].What }
    }
    return $points
}

# The screen point for a client offset: screen = client origin + offset.
function ConvertTo-ScreenPoint($Client, [int]$X, [int]$Y) {
    return [pscustomobject]@{ X = $Client.X + $X; Y = $Client.Y + $Y }
}

# The screen point of a point measured in a window of RefWidth x RefHeight, scaled to the window's actual client size
# (the Paradox launcher's layout scales with its window). A point without a reference size (a -Point override, given
# for this run's window) is used as it is.
function Get-ScaledScreenPoint($Client, $Point) {
    if ($Point.RefWidth -and $Point.RefHeight) {
        $x = [int][Math]::Round($Point.X * $Client.Width / $Point.RefWidth)
        $y = [int][Math]::Round($Point.Y * $Client.Height / $Point.RefHeight)
        return ConvertTo-ScreenPoint $Client $x $y
    }
    return ConvertTo-ScreenPoint $Client $Point.X $Point.Y
}

# "client: x,y,w,h" from game-control.ps1 window's output, as an object; $null when absent.
function ConvertFrom-WindowOutput([string]$Text) {
    if ($Text -match '(?m)^client:\s*(-?\d+),(-?\d+),(\d+),(\d+)') {
        return [pscustomobject]@{ X = [int]$Matches[1]; Y = [int]$Matches[2]; Width = [int]$Matches[3]; Height = [int]$Matches[4] }
    }
    return $null
}

# Why the client area can't be driven with these offsets, or $null when it is the expected 2560x1440.
function Get-ClientSizeProblem($Client) {
    if (-not $Client) { return "game-control.ps1 window printed no client rect" }
    if ($Client.Width -ne $GameRunClientWidth -or $Client.Height -ne $GameRunClientHeight) {
        return "the game window's client area is $($Client.Width)x$($Client.Height), not ${GameRunClientWidth}x$GameRunClientHeight`: every offset assumes that size. Set the game windowed at 2560x1440 (Options > Graphics) and run again."
    }
    return $null
}

# Why the scripted run must not start, or $null: the game is one shared instance
# (knowledge/orchestration.md#the-shared-game-instance), so a run only starts when check-batch-status would say
# "Game: not running".
function Get-GameClaimProblem {
    $game = Get-GameProcess
    if ($game) {
        $started = try { $game.StartTime.ToString('yyyy-MM-dd HH:mm') } catch { '?' }
        return "Game: RUNNING ($($game.ProcessName) pid $($game.Id), started $started). The game is one shared instance: the user or another session has it. Ask before closing it; this script never closes an instance it didn't launch."
    }
    return $null
}

# The deployed mod's build-info.txt lines (branch, commit, time), or one line saying it's unknown.
function Get-DeployedBuildInfo([string]$ModsPath) {
    if (-not $ModsPath) { $ModsPath = [Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH', 'User') }
    if (-not $ModsPath) { $ModsPath = $env:CSII_LOCALMODSPATH }
    $info = if ($ModsPath) { Join-Path $ModsPath 'FiscalOverhaul\build-info.txt' } else { $null }
    if ($info -and (Test-Path -LiteralPath $info)) { return @(Get-Content -LiteralPath $info | Where-Object { $_.Trim() }) }
    return @('unknown (no build-info.txt: not deployed by tools/build.ps1)')
}

# The game's LocalLow folder (FiscalOverhaul.coc, Logs/).
function Get-GameDataDir {
    if ($env:FO_GAME_DATA_DIR) { return $env:FO_GAME_DATA_DIR }
    return Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
}

# --- pixel tests on screenshots (System.Drawing) ---

function Use-Drawing { Add-Type -AssemblyName System.Drawing }

# The share (0-1) of pixels in a rectangle of an image that $Test accepts; $Test gets a System.Drawing.Color.
function Get-PixelShare([System.Drawing.Bitmap]$Bitmap, [int]$Left, [int]$Top, [int]$Width, [int]$Height, [scriptblock]$Test) {
    $hits = 0; $total = 0
    for ($y = [Math]::Max(0, $Top); $y -lt [Math]::Min($Bitmap.Height, $Top + $Height); $y += 2) {
        for ($x = [Math]::Max(0, $Left); $x -lt [Math]::Min($Bitmap.Width, $Left + $Width); $x += 2) {
            $total++
            if (& $Test $Bitmap.GetPixel($x, $y)) { $hits++ }
        }
    }
    if ($total -eq 0) { return 0 }
    return $hits / $total
}

# Whether a full-scale client screenshot shows the PAUSED banner: the cyan (about 74,192,240) box top centre.
# Measured on the Odiham save (loaded.png): the banner spans about x 1170-1390, y 4-48 of the 2560-wide client.
function Test-PausedBanner([string]$Path) {
    Use-Drawing
    $bitmap = [System.Drawing.Bitmap]::FromFile($Path)
    try {
        $mid = [int]($bitmap.Width / 2)
        $share = Get-PixelShare $bitmap ($mid - 70) 12 140 24 { param($c) $c.B -ge 200 -and $c.G -ge 150 -and $c.R -le 130 }
        return $share -ge 0.4
    }
    finally { $bitmap.Dispose() }
}

# Whether a Budget row's check square is filled: its colour when on, the panel's grey-blue (about 60,73,91) when off.
# Saturation decides: every series colour spreads its channels by 60 or more (Tile Upkeep 94,4,48), the off grey by 35.
function Test-RowChecked([System.Drawing.Bitmap]$Bitmap, [int]$X, [int]$Y) {
    $c = $Bitmap.GetPixel($X, $Y)
    $max = [Math]::Max($c.R, [Math]::Max($c.G, $c.B))
    $min = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
    return ($max - $min) -ge 60
}

# The names of the Budget rows whose check square reads off in a full-scale client screenshot.
function Get-UncheckedRows([string]$Path, $Points) {
    Use-Drawing
    $bitmap = [System.Drawing.Bitmap]::FromFile($Path)
    try {
        $x = $Points.RowCheckX.X
        return @($GameRunBudgetRows | Where-Object { -not (Test-RowChecked $bitmap $x $Points[$_].Y) })
    }
    finally { $bitmap.Dispose() }
}

# Crops a client screenshot to a box and scales it to Width x Height (high-quality bicubic), as a 24-bit RGB PNG:
# the captures are opaque, and the default 32-bit ARGB PNG of a game shot can pass Paradox Mods' 2.1 MB image limit.
function Save-CroppedImage([string]$Source, [string]$Out, $Box, [int]$Width, [int]$Height) {
    Use-Drawing
    $bitmap = [System.Drawing.Bitmap]::FromFile($Source)
    try {
        if ($Box) {
            if ($Box.Right -gt $bitmap.Width -or $Box.Bottom -gt $bitmap.Height) {
                throw "the crop box ($($Box.Left),$($Box.Top))-($($Box.Right),$($Box.Bottom)) doesn't fit $Source ($($bitmap.Width)x$($bitmap.Height))"
            }
            $sourceRect = [System.Drawing.Rectangle]::new($Box.Left, $Box.Top, $Box.Right - $Box.Left, $Box.Bottom - $Box.Top)
        }
        else { $sourceRect = [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height) }
        $image = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($image)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
                $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # no dark fringe at the edges
                $g.DrawImage($bitmap, [System.Drawing.Rectangle]::new(0, 0, $Width, $Height), $sourceRect.X, $sourceRect.Y,
                    $sourceRect.Width, $sourceRect.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
            }
            finally { $g.Dispose() }
            $parent = Split-Path -Parent $Out
            if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
            $image.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $image.Dispose() }
    }
    finally { $bitmap.Dispose() }
}

# --- the steps grammar (in-game-check.ps1 -Steps; store-screenshots.ps1's own sequence uses the same verbs) ---
#   click <point>  | right-click <point> | move <point> | hover <point>
#   key <name>                       a System.Windows.Forms.Keys name (Escape, Space, Enter, ControlKey+S ...)
#   wait <ms>                        up to 10000
#   screenshot <name> [what it shows ...]   <Out>/<name>.png, the window at half scale after a 1500 ms settle
# A <point> is @Name (a named point) or x,y (client offsets in the 2560x1440 window). '#' starts a comment.
$GameRunStepVerbs = 'click', 'right-click', 'move', 'hover', 'key', 'wait', 'screenshot'

function ConvertFrom-GameRunSteps([string[]]$Lines, $Points) {
    $steps = New-Object System.Collections.Generic.List[object]
    $number = 0
    foreach ($raw in $Lines) {
        $number++
        $line = ($raw -replace '#.*$', '').Trim()
        if (-not $line) { continue }
        $parts = @($line -split '\s+')
        $verb = $parts[0].ToLowerInvariant()
        $rest = @($parts | Select-Object -Skip 1)
        $where = "steps line $number ('$($raw.Trim())')"
        if ($GameRunStepVerbs -notcontains $verb) { throw "$where`: unknown verb '$($parts[0])'; one of: $($GameRunStepVerbs -join ', ')" }
        $step = [ordered]@{ Verb = $verb; Line = $number }
        switch ($verb) {
            { $_ -in 'click', 'right-click', 'move', 'hover' } {
                if ($rest.Count -ne 1) { throw "$where`: $verb takes one point (@Name or x,y)" }
                $p = $rest[0]
                if ($p -match '^@([A-Za-z]+)$') {
                    $name = $Matches[1]
                    $match = @($Points.Keys | Where-Object { $_ -eq $name })
                    if (-not $match) { throw "$where`: no point named '@$name'; the names: $($Points.Keys -join ', ')" }
                    $step.X = $Points[$match[0]].X; $step.Y = $Points[$match[0]].Y; $step.Point = $match[0]
                }
                elseif ($p -match '^(\d+),(\d+)$') {
                    $step.X = [int]$Matches[1]; $step.Y = [int]$Matches[2]; $step.Point = $p
                    if ($step.X -ge $GameRunClientWidth -or $step.Y -ge $GameRunClientHeight) { throw "$where`: $p is outside the ${GameRunClientWidth}x$GameRunClientHeight client area" }
                }
                else { throw "$where`: '$p' isn't a point: @Name or x,y (client offsets)" }
            }
            'key' {
                if ($rest.Count -ne 1 -or $rest[0] -notmatch '^[A-Za-z0-9]+(\+[A-Za-z0-9]+)*$') { throw "$where`: key takes one key name, e.g. key Escape" }
                if ($rest[0] -match '(^|\+)Space($|\+)') { throw "$where`: Space toggles the pause; the script keeps the game paused, so steps can't press it" }
                $step.Key = $rest[0]
            }
            'wait' {
                if ($rest.Count -ne 1 -or $rest[0] -notmatch '^\d+$' -or [int]$rest[0] -gt 10000) { throw "$where`: wait takes milliseconds, 0 to 10000" }
                $step.Ms = [int]$rest[0]
            }
            'screenshot' {
                if ($rest.Count -lt 1 -or $rest[0] -notmatch '^[a-z0-9][a-z0-9-]*$') { throw "$where`: screenshot takes a name (lower case, digits, hyphens), then what it shows" }
                $step.Name = $rest[0]
                $step.What = ($rest | Select-Object -Skip 1) -join ' '
            }
        }
        $steps.Add([pscustomobject]$step)
    }
    return , $steps
}

# --- driving the game ---

# Runs game-control.ps1 (or FO_GAME_CONTROL's stand-in) in this process; returns its output, throws on a failure
# (game-control prints its reason on stderr first).
function Invoke-GameControl {
    $script = if ($env:FO_GAME_CONTROL) { $env:FO_GAME_CONTROL } else { Join-Path $PSScriptRoot 'game-control.ps1' }
    $global:LASTEXITCODE = 0
    $output = & $script @args 2>&1 | Out-String
    $code = $LASTEXITCODE
    $text = $output.Trim()
    if ($text) { Write-Host "  $($text -replace "`n", "`n  ")" -ForegroundColor DarkGray }
    if ($code -ne 0) { throw "game-control $($args -join ' ') failed (exit $code)$(if ($text) { ": $text" })" }
    return $output
}

# The session a run drives: where the client is, what it launched, the points.
function New-GameRunSession($Points) {
    return [pscustomobject]@{ Points = $Points; Client = $null; Launched = $false; LaunchedAt = $null }
}

function Get-SessionPoint($Session, [string]$Name) {
    $p = $Session.Points[$Name]
    return ConvertTo-ScreenPoint $Session.Client $p.X $p.Y
}

function Invoke-GameClick($Session, [string]$Name, [switch]$Right) {
    $p = Get-SessionPoint $Session $Name
    $extra = if ($Right) { @('-Right') } else { @() }
    Invoke-GameControl click -X $p.X -Y $p.Y @extra | Out-Null
}

function Invoke-GameMove($Session, [string]$Name, [switch]$Hover) {
    $p = Get-SessionPoint $Session $Name
    $verb = if ($Hover) { 'hover' } else { 'move' }
    Invoke-GameControl $verb -X $p.X -Y $p.Y | Out-Null
}

# A screenshot of the game window's client area.
function Save-GameShot([string]$Out, [double]$Scale = 0.5, [int]$Delay = 1500) {
    Invoke-GameControl screenshot -Out $Out -Window -Scale $Scale -Delay $Delay | Out-Null
}

# Launches the game through Steam, resumes the last save from the Paradox launcher, waits for the load and reads the
# client rect. Check shots (the launcher, the loaded game) go to $CheckDir. Sets $Session.Launched first, so a
# failure after the launch still quits what this run started.
function Start-GameRun($Session, [string]$CheckDir) {
    $Session.LaunchedAt = (Get-Date).AddSeconds(-2)
    $Session.Launched = $true
    if ($env:FO_GAME_LAUNCH) {
        $launchArgs = if ($env:FO_GAME_LAUNCH_ARGS) { $env:FO_GAME_LAUNCH_ARGS } else { $null }
        if ($launchArgs) { Start-Process -FilePath $env:FO_GAME_LAUNCH -ArgumentList $launchArgs -WindowStyle Hidden | Out-Null }
        else { Start-Process -FilePath $env:FO_GAME_LAUNCH -WindowStyle Hidden | Out-Null }
    }
    else {
        Write-Host "Launching through Steam ($GameRunSteamUri)."
        Start-Process $GameRunSteamUri
    }
    if (-not $env:FO_GAME_LAUNCH -or $env:FO_GAME_LAUNCH_VIA_LAUNCHER) {
        Invoke-GameControl wait-process -Title $GameRunLauncherTitle -Timeout 120 | Out-Null
        # Resume, and once more if the game's window doesn't come (a click that missed, or a launcher still drawing).
        $attempt = 0
        while ($true) {
            $attempt++
            $shot = if ($attempt -eq 1) { 'launcher.png' } else { "launcher-$attempt.png" }
            Invoke-GameControl focus -Title $GameRunLauncherTitle | Out-Null
            Invoke-GameControl screenshot -Out (Join-Path $CheckDir $shot) -Window -Title $GameRunLauncherTitle -Scale 0.5 -Delay 3000 | Out-Null
            $launcher = ConvertFrom-WindowOutput (Invoke-GameControl window -Title $GameRunLauncherTitle)
            if (-not $launcher) { throw 'game-control window -Title printed no client rect for the Paradox launcher' }
            $resume = Get-ScaledScreenPoint $launcher $Session.Points.LauncherResume
            Write-Host "Resume at $($resume.X),$($resume.Y) (launcher client $($launcher.Width)x$($launcher.Height); attempt $attempt)."
            Invoke-GameControl click -X $resume.X -Y $resume.Y | Out-Null
            try { Invoke-GameControl wait-process -Timeout 60 | Out-Null; break }
            catch {
                if ($attempt -ge 2) { throw "the game's window didn't come after two clicks on Resume: look at $shot and correct LauncherResume (-Point LauncherResume=x,y, in this launcher's client)" }
                Write-Host 'No game window 60 s after Resume: clicking it again.' -ForegroundColor Yellow
            }
        }
    }
    Invoke-GameControl wait-process -Timeout 120 | Out-Null
    Invoke-GameControl wait-loaded -Timeout 300 | Out-Null
    Invoke-GameControl focus | Out-Null
    $Session.Client = ConvertFrom-WindowOutput (Invoke-GameControl window)
    $problem = Get-ClientSizeProblem $Session.Client
    if ($problem) { throw $problem }
}

# Makes sure the simulation is paused, from the PAUSED banner in a full-scale shot: Space only when the banner is
# missing, then checks again. Never a blind Space (the save loads paused; Space would start it).
function Assert-GamePaused($Session, [string]$CheckDir) {
    $shot = Join-Path $CheckDir 'loaded.png'
    Save-GameShot -Out $shot -Scale 1 -Delay 2000
    if (Test-PausedBanner $shot) { Write-Host 'The game is paused (PAUSED banner).'; return }
    Write-Host 'No PAUSED banner: pressing Space once to pause.' -ForegroundColor Yellow
    Invoke-GameControl focus | Out-Null
    Invoke-GameControl key Space | Out-Null
    $again = Join-Path $CheckDir 'paused.png'
    Save-GameShot -Out $again -Scale 1 -Delay 1000
    if (-not (Test-PausedBanner $again)) { throw "the game isn't paused after one Space (no PAUSED banner in $again): look at loaded.png and paused.png" }
}

# Quits the game this run launched, and only that: $Escapes presses of Escape reach the pause menu (2 with a panel
# open), then Exit and Exit to Desktop; when the process isn't gone 30 s later, or -Force (a failed run, whose state
# is unknown), the launched instance is stopped. Returns $true when no game process is left.
function Stop-GameRun($Session, [string]$CheckDir, [int]$Escapes = 2, [switch]$Force) {
    if (-not $Session.Launched) { return -not (Get-GameProcess) }
    $name = if ($env:FO_GAME_PROCESS_NAME) { $env:FO_GAME_PROCESS_NAME } else { 'Cities2' }
    $ours = { @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { try { $_.StartTime -ge $Session.LaunchedAt } catch { $false } }) }
    if (-not $Force -and $Session.Client -and (& $ours).Count) {
        try {
            Invoke-GameControl focus | Out-Null
            for ($i = 0; $i -lt $Escapes; $i++) { Invoke-GameControl key Escape | Out-Null; Start-Sleep -Milliseconds 700 }
            if ($CheckDir) { Save-GameShot -Out (Join-Path $CheckDir 'quit-menu.png') -Delay 1000 }
            Invoke-GameClick $Session 'PauseExit'
            Start-Sleep -Milliseconds 1500
            if ($CheckDir) { Save-GameShot -Out (Join-Path $CheckDir 'quit-dialog.png') -Delay 0 }
            Invoke-GameClick $Session 'ExitToDesktop'
            Invoke-GameControl wait-process -Gone -Timeout 30 | Out-Null
        }
        catch { Write-Host "Quitting from the menu didn't end the game ($($_.Exception.Message)); stopping it." -ForegroundColor Yellow }
    }
    foreach ($process in (& $ours)) {
        Write-Host "Stopping the instance this run launched (pid $($process.Id))." -ForegroundColor Yellow
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        try { $process.WaitForExit(30000) | Out-Null } catch { }
    }
    $left = Get-GameProcess
    if ($left) { Write-Host "Game: RUNNING (pid $($left.Id)) - not an instance this run launched, so it was left alone." -ForegroundColor Yellow; return $false }
    Write-Host 'Game: not running' -ForegroundColor Green
    return $true
}

# Runs parsed steps: points are client offsets; screenshots go to $OutDir and are returned as manifest entries.
function Invoke-GameRunSteps($Session, $Steps, [string]$OutDir) {
    $shots = New-Object System.Collections.Generic.List[object]
    foreach ($step in $Steps) {
        switch ($step.Verb) {
            { $_ -in 'click', 'right-click' } {
                $p = ConvertTo-ScreenPoint $Session.Client $step.X $step.Y
                $extra = if ($step.Verb -eq 'right-click') { @('-Right') } else { @() }
                Invoke-GameControl click -X $p.X -Y $p.Y @extra | Out-Null
            }
            { $_ -in 'move', 'hover' } {
                $p = ConvertTo-ScreenPoint $Session.Client $step.X $step.Y
                Invoke-GameControl $step.Verb -X $p.X -Y $p.Y | Out-Null
            }
            'key' { Invoke-GameControl focus | Out-Null; Invoke-GameControl key $step.Key | Out-Null }
            'wait' { Start-Sleep -Milliseconds $step.Ms }
            'screenshot' {
                Save-GameShot -Out (Join-Path $OutDir "$($step.Name).png")
                $shots.Add([pscustomobject]@{ Name = "$($step.Name).png"; What = $step.What })
            }
        }
    }
    return , $shots
}

# --- the store set (store-screenshots.ps1; knowledge/releasing.md#screenshots) ---

# The publish set: base names (the files' names before the version suffix), what each shows (the README's alt text
# is kept as it is), and Kind: panel = cropped to the 16:9 box around the Economy panel, options = the window whole.
$StoreShotSet = @(
    [pscustomobject]@{ Base = '01-budget-chart';          Kind = 'panel';   What = 'Economy > Budget, every row on: all the lines' }
    [pscustomobject]@{ Base = '02-budget-tooltip';        Kind = 'panel';   What = 'the chart tooltip on a line, and the Change/Average overlay' }
    [pscustomobject]@{ Base = '03-taxation-density';      Kind = 'panel';   What = 'Economy > Taxation, Residential By density open, with the run''s offsets' }
    [pscustomobject]@{ Base = '04-options';               Kind = 'options'; What = 'Options > Fiscal Overhaul: the feature switches' }
    [pscustomobject]@{ Base = '05-budget-vanilla-detail'; Kind = 'panel';   What = 'the Taxes row hovered: the vanilla detail panel popped out' }
    [pscustomobject]@{ Base = '06-budget-district';       Kind = 'panel';   What = 'the first district selected (one click on the right arrow)' }
    [pscustomobject]@{ Base = '07-budget-district-list';  Kind = 'panel';   What = 'the district list open' }
)
$StoreSuffixPattern = '-v\d+-\d+-\d+$'

function ConvertTo-VersionKey([string]$Version) {
    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { return $null }
    return [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
}

# The version the screenshot names carry: -Version when given; else ModVersion, unless a v<version> tag at or above
# it says that version is already published - then the next patch after the highest tag (promote.py plan's rule), so
# the new names never equal the ones the last release uploaded.
function Get-ScreenshotVersion([string]$Declared, [string[]]$Tags = @(), [string]$Override) {
    if ($Override) {
        if (-not (ConvertTo-VersionKey $Override)) { throw "-Version '$Override' isn't X.Y.Z." }
        return $Override
    }
    $declaredKey = ConvertTo-VersionKey $Declared
    if (-not $declaredKey) { throw "ModVersion '$Declared' in PublishConfiguration.xml isn't X.Y.Z: give -Version." }
    $tagKeys = @($Tags | Where-Object { $_ } | ForEach-Object { ConvertTo-VersionKey ($_.Trim() -replace '^v', '') } | Where-Object { $_ } | Sort-Object -Descending)
    if ($tagKeys.Count -and $tagKeys[0] -ge $declaredKey) {
        return '{0}.{1}.{2}' -f $tagKeys[0].Major, $tagKeys[0].Minor, ($tagKeys[0].Build + 1)
    }
    return $Declared
}

# 01-budget-chart + 1.2.0 -> 01-budget-chart-v1-2-0.png
function Get-StoreFileName([string]$Base, [string]$Version) { return "$Base-v$($Version -replace '\.', '-').png" }

# Writes UTF-8 text over a file, keeping whether it starts with a byte-order mark (PublishConfiguration.xml has one,
# the README hasn't); a new file gets none.
function Write-TextKeepingBom([string]$Path, [string]$Text) {
    $bom = $false
    if (Test-Path -LiteralPath $Path) {
        $stream = [IO.File]::OpenRead($Path)
        try { $head = [byte[]]::new(3); $read = $stream.Read($head, 0, 3) } finally { $stream.Dispose() }
        $bom = $read -eq 3 -and $head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF
    }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($bom)))
}

# A screenshot file's base name: the name without .png and any -vX-Y-Z suffix.
function Get-StoreBase([string]$FileName) {
    return ([IO.Path]::GetFileNameWithoutExtension($FileName)) -replace $StoreSuffixPattern, ''
}

# PublishConfiguration.xml's text with each <Screenshot Value="Properties/Screenshots/<file>"> whose base is in
# $NewNames (base -> new file name) pointing at the new name; the order and everything else stay as they are.
function Update-StoreConfigText([string]$Text, [hashtable]$NewNames) {
    $pattern = '(<Screenshot\s+Value=")(Properties/Screenshots/)([^"/]+\.png)(")'
    return [regex]::Replace($Text, $pattern, {
            param($m)
            $base = Get-StoreBase $m.Groups[3].Value
            if ($NewNames.ContainsKey($base)) { return $m.Groups[1].Value + $m.Groups[2].Value + $NewNames[$base] + $m.Groups[4].Value }
            return $m.Value
        })
}

# The README's text with each Mod/Properties/Screenshots/<file>.png whose base is in $NewNames renamed.
function Update-StoreReadmeText([string]$Text, [hashtable]$NewNames) {
    return [regex]::Replace($Text, '(Mod/Properties/Screenshots/)([^)\s"/]+\.png)', {
            param($m)
            $base = Get-StoreBase $m.Groups[2].Value
            if ($NewNames.ContainsKey($base)) { return $m.Groups[1].Value + $NewNames[$base] }
            return $m.Value
        })
}

# FiscalOverhaul.coc's text (the settings name on the first line, then a JSON object) with the features the store set
# shows switched on and the density offsets set; other keys are kept. Empty text = no file yet.
function Set-StoreSettingsText([string]$Text, [string]$Offsets) {
    if ($Offsets -notmatch '^\s*-?\d+(\s*,\s*-?\d+){11}\s*$') { throw "-Offsets '$Offsets' isn't 12 comma-separated integers (residential, commercial, industrial, office; low, medium, high each)." }
    $name = 'FiscalOverhaul'
    $values = [ordered]@{}
    if ($Text -and $Text.Trim()) {
        $brace = $Text.IndexOf('{')
        if ($brace -lt 0) { throw "FiscalOverhaul.coc has no '{': not the settings format this script knows; move it aside and run again." }
        $head = $Text.Substring(0, $brace).Trim()
        if ($head) { $name = $head }
        $parsed = $Text.Substring($brace) | ConvertFrom-Json
        foreach ($property in $parsed.PSObject.Properties) { $values[$property.Name] = $property.Value }
    }
    $values['EnableBudgetChart'] = $true
    $values['EnableDistrictBudget'] = $true
    $values['EnableDensityTaxes'] = $true
    $values['DensityTaxOffsets'] = ($Offsets -replace '\s', '')
    $lines = foreach ($key in $values.Keys) {
        $v = $values[$key]
        $json = if ($v -is [bool]) { $v.ToString().ToLowerInvariant() } else { ConvertTo-Json -InputObject $v -Compress }
        "    `"$key`": $json"
    }
    return "$name`n{`n" + ($lines -join ",`n") + "`n}`n"
}
