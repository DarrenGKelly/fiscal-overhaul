<#
.SYNOPSIS
    Tests tools/game-control.ps1 without the game.

.DESCRIPTION
    Runs the tool in child shells against the real desktop and a stand-in window: a small borderless, topmost form
    in a solid colour, shown by a child shell, which records the keys and clicks it receives. What must hold:
    bad arguments exit 2; a screenshot is a PNG of the primary monitor's size, a -Region one is cropped to the
    region (the form's colour inside the form, not outside it) and -Scale shrinks it; window/focus find a window by
    -Title and fail cleanly when no title matches or the game (FO_GAME_PROCESS_NAME, a fake name) isn't running;
    move lands where asked (GetCursorPos), move-relative moves the way asked, hover ends on the point; key and click
    reach the focused window; wait-process waits for a window and times out. Every check has a pass and a fail
    case. The cursor goes back where it was. Takes about 30 s and briefly shows the form at 200,200.
        ./tools/test-game-control.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$tool = Join-Path $toolsDir 'game-control.ps1'

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

$shell = (Get-Process -Id $PID).Path
function Invoke-Tool([string[]]$Arguments, [string]$GameName = 'fo-no-such-game') {
    $ErrorActionPreference = 'Continue'
    $saved = $env:FO_GAME_PROCESS_NAME
    $env:FO_GAME_PROCESS_NAME = $GameName
    try {
        $global:LASTEXITCODE = 0
        $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally { $env:FO_GAME_PROCESS_NAME = $saved }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FoGameControlTest {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[void][FoGameControlTest]::SetProcessDPIAware()
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
function Get-Cursor { $p = New-Object FoGameControlTest+POINT; [void][FoGameControlTest]::GetCursorPos([ref]$p); $p }
function Read-Png([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $isPng = $bytes.Length -gt 8 -and (($bytes[0..7] -join ',') -eq '137,80,78,71,13,10,26,10')
    $image = [System.Drawing.Bitmap]::new($Path)
    return [pscustomobject]@{ IsPng = $isPng; Width = $image.Width; Height = $image.Height; Image = $image }
}
function Test-Colour($Pixel, $Colour) {
    [Math]::Abs($Pixel.R - $Colour.R) -le 8 -and [Math]::Abs($Pixel.G - $Colour.G) -le 8 -and [Math]::Abs($Pixel.B - $Colour.B) -le 8
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-game-control-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $root | Out-Null
$formTitle = 'fo-game-control-test-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$formRect = [System.Drawing.Rectangle]::new(200, 200, 400, 300)
$colour = [System.Drawing.Color]::FromArgb(12, 200, 34)
$events = Join-Path $root 'events.txt'
$ready = Join-Path $root 'ready.txt'
$formScript = Join-Path $root 'form.ps1'
@"
Add-Type -Name Dpi -Namespace FoTest -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][FoTest.Dpi]::SetProcessDPIAware()
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
`$form = New-Object System.Windows.Forms.Form
`$form.Text = '$formTitle'
`$form.FormBorderStyle = 'None'
`$form.StartPosition = 'Manual'
`$form.TopMost = `$true
`$form.ShowInTaskbar = `$false
`$form.Bounds = New-Object System.Drawing.Rectangle($($formRect.X), $($formRect.Y), $($formRect.Width), $($formRect.Height))
`$form.BackColor = [System.Drawing.Color]::FromArgb($($colour.R), $($colour.G), $($colour.B))
`$form.KeyPreview = `$true
`$form.Add_KeyDown({ Add-Content -LiteralPath '$events' "key `$(`$_.KeyCode)" })
`$form.Add_MouseDown({ Add-Content -LiteralPath '$events' "mouse `$(`$_.Button) `$(`$_.X),`$(`$_.Y)" })
`$form.Add_Shown({ Set-Content -LiteralPath '$ready' 'ready' })
[void]`$form.ShowDialog()
"@ | Set-Content -LiteralPath $formScript

$cursorBefore = Get-Cursor
$formProcess = $null
try {
    # --- Arguments -----------------------------------------------------------------------------------------------
    $r = Invoke-Tool @()
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'no command') 'no command exits 2 and lists the commands' $r.Output
    $r = Invoke-Tool @('launch-rockets')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "unknown command 'launch-rockets'") 'an unknown command exits 2' $r.Output
    $r = Invoke-Tool @('screenshot')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'needs -Out') 'screenshot without -Out exits 2' $r.Output
    $r = Invoke-Tool @('screenshot', '-Out', "$root\x.png", '-Region', '1,2,3')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'four integers') 'a three-number -Region exits 2' $r.Output
    $r = Invoke-Tool @('screenshot', '-Out', "$root\x.png", '-Region', '0,0,0,10')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'positive') 'a zero-width -Region exits 2' $r.Output
    $r = Invoke-Tool @('screenshot', '-Out', "$root\x.png", '-Scale', '2')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'Scale') 'a -Scale above 1 exits 2' $r.Output
    $r = Invoke-Tool @('screenshot', '-Out', "$root\x.png", '-Region', '0,0,5,5', '-Window')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'not both') '-Region with -Window exits 2' $r.Output
    Check (-not (Test-Path "$root\x.png")) 'no screenshot is written after a bad argument'
    $r = Invoke-Tool @('click', '-X', '5')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'needs -Y') 'click without -Y exits 2' $r.Output
    $r = Invoke-Tool @('move-relative', '-Dx', '5', '-Dy', '5', '-Steps', '0')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'Steps') 'move-relative with -Steps 0 exits 2' $r.Output
    $r = Invoke-Tool @('key')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'needs a key name') 'key without a name exits 2' $r.Output
    $r = Invoke-Tool @('key', 'NotAKey')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "unknown key 'NotAKey'") 'an unknown key name exits 2 (and sends nothing)' $r.Output

    # --- Screenshots of the desktop ------------------------------------------------------------------------------
    $primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $full = Join-Path $root 'full.png'
    $r = Invoke-Tool @('screenshot', '-Out', $full)
    Check ($r.ExitCode -eq 0 -and (Test-Path $full)) 'screenshot writes the file' $r.Output
    if (Test-Path $full) {
        $png = Read-Png $full
        Check $png.IsPng 'the screenshot is a PNG'
        Check ($png.Width -eq $primary.Width -and $png.Height -eq $primary.Height) "the screenshot is the primary monitor's size ($($primary.Width)x$($primary.Height))" "got $($png.Width)x$($png.Height)"
        $png.Image.Dispose()
    }
    $half = Join-Path $root 'half.png'
    $r = Invoke-Tool @('screenshot', '-Out', $half, '-Scale', '0.5')
    if (Test-Path $half) {
        $png = Read-Png $half
        Check ($png.Width -eq [Math]::Round($primary.Width / 2) -and $png.Height -eq [Math]::Round($primary.Height / 2)) '-Scale 0.5 halves the screenshot' "got $($png.Width)x$($png.Height)"
        $png.Image.Dispose()
    }
    else { Check $false '-Scale 0.5 writes a screenshot' $r.Output }

    # --- The stand-in window -------------------------------------------------------------------------------------
    $formProcess = Start-Process $shell -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $formScript -WindowStyle Hidden -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $ready) -and $clock.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 200 }
    Check (Test-Path $ready) 'the stand-in window is shown'
    Start-Sleep -Milliseconds 500

    $r = Invoke-Tool @('window', '-Title', [regex]::Escape($formTitle))
    $expected = '{0},{1},{2},{3}' -f $formRect.X, $formRect.Y, $formRect.Width, $formRect.Height
    Check ($r.ExitCode -eq 0 -and $r.Output -match "rect:\s+$expected" -and $r.Output -match "client:\s+$expected") "window -Title prints the window's rect ($expected)" $r.Output
    $r = Invoke-Tool @('window', '-Title', 'fo-no-window-has-this-title')
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'no visible window title matches' -and $r.Output -match [regex]::Escape($formTitle)) 'window -Title with no match exits 1 and lists the visible windows' $r.Output
    $r = Invoke-Tool @('window')
    Check ($r.ExitCode -eq 1 -and $r.Output -match "no fo-no-such-game window: the game isn't running") 'window with no game process exits 1 and says so' $r.Output
    $r = Invoke-Tool @('focus')
    Check ($r.ExitCode -eq 1 -and $r.Output -match "no fo-no-such-game window") 'focus with no game process exits 1 and says so' $r.Output
    $r = Invoke-Tool @('screenshot', '-Out', "$root\nogame.png", '-Window')
    Check ($r.ExitCode -eq 1 -and -not (Test-Path "$root\nogame.png")) 'screenshot -Window with no game exits 1 and writes nothing' $r.Output

    # Region cropping: inside the form is the form's colour, at the place the full screenshot has it too.
    $inside = Join-Path $root 'inside.png'
    $r = Invoke-Tool @('screenshot', '-Out', $inside, '-Region', ('{0},{1},100,60' -f ($formRect.X + 50), ($formRect.Y + 40)))
    if (Test-Path $inside) {
        $png = Read-Png $inside
        Check ($png.Width -eq 100 -and $png.Height -eq 60) '-Region crops to its size (100x60)' "got $($png.Width)x$($png.Height)"
        $allColour = (Test-Colour $png.Image.GetPixel(0, 0) $colour) -and (Test-Colour $png.Image.GetPixel(99, 59) $colour) -and (Test-Colour $png.Image.GetPixel(50, 30) $colour)
        Check $allColour '-Region captures the screen at the region (the stand-in window colour)' ("pixel 0,0 is " + $png.Image.GetPixel(0, 0))
        $png.Image.Dispose()
    }
    else { Check $false '-Region writes a screenshot' $r.Output }
    $outside = Join-Path $root 'outside.png'
    $r = Invoke-Tool @('screenshot', '-Out', $outside, '-Region', ('{0},{1},40,40' -f ($formRect.Right + 10), ($formRect.Bottom + 10)))
    if (Test-Path $outside) {
        $png = Read-Png $outside
        Check (-not (Test-Colour $png.Image.GetPixel(20, 20) $colour)) 'a -Region beside the window does not show its colour'
        $png.Image.Dispose()
    }
    else { Check $false '-Region beside the window writes a screenshot' $r.Output }
    $windowShot = Join-Path $root 'window.png'
    $r = Invoke-Tool @('screenshot', '-Out', $windowShot, '-Window', '-Title', [regex]::Escape($formTitle))
    if (Test-Path $windowShot) {
        $png = Read-Png $windowShot
        Check ($png.Width -eq $formRect.Width -and $png.Height -eq $formRect.Height -and (Test-Colour $png.Image.GetPixel(10, 10) $colour)) 'screenshot -Window -Title captures that window''s client area' "got $($png.Width)x$($png.Height)"
        $png.Image.Dispose()
    }
    else { Check $false 'screenshot -Window -Title writes a screenshot' $r.Output }

    # --- The cursor ----------------------------------------------------------------------------------------------
    $r = Invoke-Tool @('move', '-X', '700', '-Y', '650')
    $p = Get-Cursor
    Check ($r.ExitCode -eq 0 -and $p.X -eq 700 -and $p.Y -eq 650) 'move puts the cursor at 700,650' "cursor at $($p.X),$($p.Y); $($r.Output)"
    $r = Invoke-Tool @('move', '-X', '-999999', '-Y', '-999999')
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'off every screen \(monitors: ') 'move off every screen exits 1 and names the monitors' $r.Output
    $r = Invoke-Tool @('click', '-X', '-999999', '-Y', '5')
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'off every screen') 'click off every screen exits 1 (and clicks nothing)' $r.Output
    # A person moving the mouse meanwhile breaks this one, so it gets a second try.
    foreach ($try in 1, 2) {
        [void][FoGameControlTest]::SetCursorPos(700, 650)
        $r = Invoke-Tool @('move-relative', '-Dx', '40', '-Dy', '-30', '-Steps', '4')
        $p = Get-Cursor
        if ($p.X -gt 700 -and $p.Y -lt 650 -and $p.X -lt 900 -and $p.Y -gt 450) { break }
    }
    Check ($r.ExitCode -eq 0 -and $p.X -gt 700 -and $p.Y -lt 650 -and $p.X -lt 900 -and $p.Y -gt 450) 'move-relative moves right and up for +Dx, -Dy (pointer acceleration allowed)' "cursor at $($p.X),$($p.Y); $($r.Output)"
    $r = Invoke-Tool @('move-relative', '-Dx', '0', '-Dy', '0')
    $q = Get-Cursor
    Check ($r.ExitCode -eq 0 -and $q.X -eq $p.X -and $q.Y -eq $p.Y) 'move-relative by 0,0 leaves the cursor still' "cursor at $($q.X),$($q.Y)"
    $r = Invoke-Tool @('hover', '-X', '650', '-Y', '600')
    $p = Get-Cursor
    Check ($r.ExitCode -eq 0 -and $p.X -eq 650 -and $p.Y -eq 600) 'hover ends on the point' "cursor at $($p.X),$($p.Y); $($r.Output)"

    # --- Focus, keys and clicks reach the window -----------------------------------------------------------------
    $r = Invoke-Tool @('focus', '-Title', [regex]::Escape($formTitle))
    $focused = $r.ExitCode -eq 0 -and $r.Output -match 'is in front'
    Check $focused 'focus -Title brings the stand-in window to the front' $r.Output
    if ($focused) {
        $r = Invoke-Tool @('key', 'F7')
        $r2 = Invoke-Tool @('key', 'ShiftKey+B')
        $r3 = Invoke-Tool @('click', '-X', ($formRect.X + 30), '-Y', ($formRect.Y + 20))
        $r4 = Invoke-Tool @('click', '-X', ($formRect.X + 60), '-Y', ($formRect.Y + 40), '-Right')
        Start-Sleep -Milliseconds 500
        $log = if (Test-Path $events) { Get-Content -LiteralPath $events -Raw } else { '' }
        Check ($r.ExitCode -eq 0 -and $log -match 'key F7') 'key F7 reaches the focused window' "$($r.Output)events: $log"
        Check ($r2.ExitCode -eq 0 -and $log -match 'key ShiftKey' -and $log -match 'key B') 'key ShiftKey+B presses both' "$($r2.Output)events: $log"
        Check ($log -notmatch 'key F8') 'only the keys sent arrive (no F8)' "events: $log"
        Check ($r3.ExitCode -eq 0 -and $log -match 'mouse Left 30,20') 'click lands at the point, in window coordinates 30,20' "$($r3.Output)events: $log"
        Check ($r4.ExitCode -eq 0 -and $log -match 'mouse Right 60,40' -and $log -notmatch 'mouse Right 30,20') 'click -Right is a right click, at its own point' "$($r4.Output)events: $log"
    }

    # --- wait-process --------------------------------------------------------------------------------------------
    $r = Invoke-Tool @('wait-process', '-Timeout', '1')
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'timed out after 1 s') 'wait-process times out when the game never starts' $r.Output
    $r = Invoke-Tool @('wait-process', '-Timeout', '5') -GameName $formProcess.ProcessName
    Check ($r.ExitCode -eq 0 -and $r.Output -match "window '$([regex]::Escape($formTitle))' up") 'wait-process returns once a process of that name has a window' $r.Output
    $r = Invoke-Tool @('wait-process', '-Gone', '-Timeout', '5')
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'gone') 'wait-process -Gone returns when the game is not running' $r.Output
}
finally {
    if ($formProcess -and -not $formProcess.HasExited) { Stop-Process -Id $formProcess.Id -Force -ErrorAction SilentlyContinue }
    [void][FoGameControlTest]::SetCursorPos($cursorBefore.X, $cursorBefore.Y)
    Start-Sleep -Milliseconds 200
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures) { Write-Host "$failures check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'all game-control checks passed'
exit 0
