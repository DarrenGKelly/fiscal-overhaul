<#
.SYNOPSIS
    Drives Cities: Skylines II from the shell on Windows: screenshots, mouse, keys, window focus. No computer use.

.DESCRIPTION
    The in-game check (knowledge/testing.md#the-in-game-check) without computer-use tools: Win32 through Add-Type,
    nothing to install. Coordinates are physical screen pixels (the process is made DPI aware), the same pixels a
    full screenshot holds, so a point read off a screenshot taken at -Scale 1 is the point to click. A screenshot at
    -Scale s or of a -Region/-Window prints its origin: screen = origin + pixel / s.

    Commands (each prints what it did; exit 0 done, 1 failed, 2 bad arguments):
      screenshot -Out <png> [-Region x,y,w,h | -Window [-Title <regex>]] [-Scale <0.1-1>]
                     the primary monitor, a screen region, or the game window (or the window -Title matches)
      click -X <x> -Y <y> [-Right] [-Double]   move there, then press and release the button
      move -X <x> -Y <y>                       put the cursor there (a real input event, checked with GetCursorPos)
      move-relative -Dx <dx> -Dy <dy> [-Steps <n>]
                     relative mouse input in n steps; Windows pointer acceleration applies, so it lands near
      hover -X <x> -Y <y>                      approach the point in small steps, then nudge by a pixel: what
                                               Gameface's hover (tooltips) needs
      key <name>[+<name>...] [-Hold <ms>]      press a key by scan code: Escape, Space, Enter, Tab, F1, Up, A, D1...
                                               (System.Windows.Forms.Keys names); "ControlKey+S" holds the first
      focus [-Title <regex>]                   bring the game window (or the window -Title matches) to the front
      window [-Title <regex>]                  print the window's rect and client rect as x,y,w,h
      windows                                  list visible top-level windows (title and process)
      wait-process [-Timeout <s>] [-Gone]      wait for the game's window to appear (or the process to end)
      cursor                                   print the cursor position

    The game window is the main window of the process Get-GameProcess names (Cities2, or $env:FO_GAME_PROCESS_NAME).

.EXAMPLE
    ./tools/game-control.ps1 screenshot -Out $env:TEMP\shot.png -Scale 0.5
.EXAMPLE
    ./tools/game-control.ps1 focus -Title 'Paradox Launcher'
.EXAMPLE
    ./tools/game-control.ps1 hover -X 1500 -Y 900
#>
param(
    [Parameter(Position = 0)][string]$Command,
    [Parameter(Position = 1)][string]$Name,
    [string]$Out,
    [string]$Region,
    [double]$Scale = 1,
    [switch]$Window,
    [string]$Title,
    [int]$X,
    [int]$Y,
    [switch]$Right,
    [switch]$Double,
    [int]$Dx,
    [int]$Dy,
    [int]$Steps = 1,
    [int]$Hold = 60,
    [int]$Timeout = 300,
    [switch]$Gone
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Stop-With([string]$Message, [int]$Code = 1) {
    [Console]::Error.WriteLine("game-control: $Message")
    exit $Code
}
function Assert-Bound([string[]]$Names) {
    foreach ($n in $Names) {
        if (-not $script:PSBoundParameters.ContainsKey($n)) { Stop-With "$Command needs -$n" 2 }
    }
}

$commands = 'screenshot', 'click', 'move', 'move-relative', 'hover', 'key', 'focus', 'window', 'windows',
    'wait-process', 'cursor'
if (-not $Command) { Stop-With "no command; one of: $($commands -join ', ') (Get-Help $PSCommandPath -Full)" 2 }
if ($Command -notin $commands) { Stop-With "unknown command '$Command'; one of: $($commands -join ', ')" 2 }

if (-not ('FoGameControl.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace FoGameControl {
public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint MOVE = 0x1, LEFTDOWN = 0x2, LEFTUP = 0x4, RIGHTDOWN = 0x8, RIGHTUP = 0x10, ABSOLUTE = 0x8000, VIRTUALDESK = 0x4000;
    const uint KEY_EXTENDED = 0x1, KEY_UP = 0x2, KEY_SCANCODE = 0x8;

    static void Send(INPUT input) {
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1)
            throw new InvalidOperationException("SendInput was blocked (error " + Marshal.GetLastWin32Error() + "; a higher-integrity window in front?)");
    }
    static INPUT Mouse(int dx, int dy, uint flags) {
        var i = new INPUT { type = INPUT_MOUSE };
        i.u.mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags };
        return i;
    }
    public static POINT Cursor() { POINT p; GetCursorPos(out p); return p; }

    // A real mouse-move input to an absolute point on the virtual desktop; SetCursorPos corrects any rounding.
    public static void MoveTo(int x, int y) {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        int nx = (int)Math.Round((x - left) * 65535.0 / (w - 1)), ny = (int)Math.Round((y - top) * 65535.0 / (h - 1));
        Send(Mouse(nx, ny, MOVE | ABSOLUTE | VIRTUALDESK));
        for (int wait = 0; wait < 10; wait++) {
            var p = Cursor();
            if (p.X == x && p.Y == y) return;
            System.Threading.Thread.Sleep(5);
        }
        SetCursorPos(x, y);
    }
    public static void MoveBy(int dx, int dy) { Send(Mouse(dx, dy, MOVE)); }
    public static void Button(bool right, bool up) {
        Send(Mouse(0, 0, right ? (up ? RIGHTUP : RIGHTDOWN) : (up ? LEFTUP : LEFTDOWN)));
    }

    static readonly HashSet<int> Extended = new HashSet<int> { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x6F, 0x90, 0xA3, 0xA5 };
    // Scan-code key input: games that read DirectInput or raw input ignore virtual-key-only events.
    public static void Key(int vk, bool up) {
        uint scan = MapVirtualKey((uint)vk, 0);
        if (scan == 0) throw new ArgumentException("no scan code for virtual key 0x" + vk.ToString("X2"));
        uint flags = KEY_SCANCODE | (up ? KEY_UP : 0) | (Extended.Contains(vk) ? KEY_EXTENDED : 0);
        var i = new INPUT { type = INPUT_KEYBOARD };
        i.u.ki = new KEYBDINPUT { wScan = (ushort)scan, dwFlags = flags };
        Send(i);
    }

    public static string Text(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, s.Capacity); return s.ToString(); }
    public static List<IntPtr> VisibleWindows() {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { if (IsWindowVisible(h) && Text(h).Length > 0) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    // The process's visible, titled top-level windows, front first (MainWindowHandle misses owned windows).
    public static List<IntPtr> WindowsOf(int pid) {
        var list = new List<IntPtr>();
        foreach (var h in VisibleWindows()) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) list.Add(h); }
        return list;
    }
    // The window's visible bounds (without the invisible resize border), falling back to GetWindowRect.
    public static RECT Bounds(IntPtr h) {
        RECT r;
        if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);
        return r;
    }
    public static RECT Client(IntPtr h) {
        RECT r; GetClientRect(h, out r);
        var p = new POINT();
        ClientToScreen(h, ref p);
        return new RECT { Left = p.X, Top = p.Y, Right = p.X + r.Right, Bottom = p.Y + r.Bottom };
    }
    // SetForegroundWindow only works for the process that has the last input; an Alt tap gives us that, and
    // attaching to the foreground thread's input covers the rest.
    public static bool Focus(IntPtr h) {
        if (IsIconic(h)) ShowWindow(h, 9);
        for (int attempt = 0; attempt < 3; attempt++) {
            if (SetForegroundWindow(h) && GetForegroundWindow() == h) return true;
            Key(0x12, false); Key(0x12, true);
            uint pid;
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid), me = GetCurrentThreadId();
            bool attached = fg != me && AttachThreadInput(fg, me, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(fg, me, false);
            System.Threading.Thread.Sleep(200);
            if (GetForegroundWindow() == h) return true;
        }
        return false;
    }
}
}
'@
}
# Physical pixels everywhere (per-monitor v2, else system aware), so coordinates match the screenshots.
if (-not [FoGameControl.Native]::SetProcessDpiAwarenessContext([IntPtr]-4)) { [void][FoGameControl.Native]::SetProcessDPIAware() }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

function Format-Rect($r) { '{0},{1},{2},{3}' -f $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top) }
function Get-ProcessName([IntPtr]$Handle) {
    $procId = [uint32]0
    [void][FoGameControl.Native]::GetWindowThreadProcessId($Handle, [ref]$procId)
    try { (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { '?' }
}

# The window -Title matches (a regex on visible top-level windows), else the game process's main window.
function Find-TargetWindow {
    if ($Title) {
        foreach ($h in [FoGameControl.Native]::VisibleWindows()) {
            if ([FoGameControl.Native]::Text($h) -match $Title) { return $h }
        }
        $titles = [FoGameControl.Native]::VisibleWindows() | ForEach-Object { "  '$([FoGameControl.Native]::Text($_))'" }
        Stop-With ("no visible window title matches '$Title'; visible windows:`n" + ($titles -join "`n"))
    }
    $name = if ($env:FO_GAME_PROCESS_NAME) { $env:FO_GAME_PROCESS_NAME } else { 'Cities2' }
    $windows = @(Get-GameWindow $name)
    if (-not $windows) {
        if (Get-GameProcess) { Stop-With "the $name process is running but has no window yet (still starting?)" }
        Stop-With "no $name window: the game isn't running (launch it with Start-Process steam://rungameid/949230)"
    }
    return $windows[0]
}

# The visible windows of every process with that name.
function Get-GameWindow([string]$ProcessName) {
    foreach ($process in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
        [FoGameControl.Native]::WindowsOf($process.Id)
    }
}

# Input to a point off every monitor would land at an edge (or nowhere); refuse it before sending anything.
function Assert-OnScreen([int]$PointX, [int]$PointY) {
    foreach ($screen in [System.Windows.Forms.Screen]::AllScreens) {
        if ($screen.Bounds.Contains($PointX, $PointY)) { return }
    }
    $all = ([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { '{0},{1},{2},{3}' -f $_.Bounds.X, $_.Bounds.Y, $_.Bounds.Width, $_.Bounds.Height }) -join '; '
    Stop-With "$PointX,$PointY is off every screen (monitors: $all)"
}

function Get-Point([string]$What) {
    $p = [FoGameControl.Native]::Cursor()
    "$What cursor at $($p.X),$($p.Y)"
}

switch ($Command) {
    'screenshot' {
        Assert-Bound 'Out'
        if ($Scale -lt 0.1 -or $Scale -gt 1) { Stop-With "-Scale must be between 0.1 and 1, not $Scale" 2 }
        if ($Region -and $Window) { Stop-With 'give -Region or -Window, not both' 2 }
        if ($Region) {
            $parts = @($Region -split '[,\s]+' | Where-Object { $_ -ne '' })
            $numbers = @($parts | Where-Object { $_ -match '^-?\d+$' } | ForEach-Object { [int]$_ })
            if ($numbers.Count -ne 4 -or $parts.Count -ne 4) { Stop-With "-Region must be x,y,w,h (four integers), not '$Region'" 2 }
            if ($numbers[2] -le 0 -or $numbers[3] -le 0) { Stop-With "-Region width and height must be positive, not '$Region'" 2 }
            $bounds = [System.Drawing.Rectangle]::new($numbers[0], $numbers[1], $numbers[2], $numbers[3])
            $what = "region $Region"
        }
        elseif ($Window) {
            $h = Find-TargetWindow
            $r = [FoGameControl.Native]::Client($h)
            $bounds = [System.Drawing.Rectangle]::new($r.Left, $r.Top, $r.Right - $r.Left, $r.Bottom - $r.Top)
            if ($bounds.Width -le 0 -or $bounds.Height -le 0) { Stop-With 'the window has no client area (minimized?)' }
            $what = "window '$([FoGameControl.Native]::Text($h))'"
        }
        else {
            $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
            $what = 'primary monitor'
        }
        $outPath = [IO.Path]::GetFullPath($Out, (Get-Location).Path)
        $parent = Split-Path -Parent $outPath
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
        $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
        try {
            $g = [System.Drawing.Graphics]::FromImage($bitmap)
            try { $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size) } finally { $g.Dispose() }
            $image = $bitmap
            if ($Scale -ne 1) {
                $w = [Math]::Max(1, [int][Math]::Round($bounds.Width * $Scale))
                $hgt = [Math]::Max(1, [int][Math]::Round($bounds.Height * $Scale))
                $image = [System.Drawing.Bitmap]::new($w, $hgt)
                $g = [System.Drawing.Graphics]::FromImage($image)
                try {
                    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $g.DrawImage($bitmap, 0, 0, $w, $hgt)
                }
                finally { $g.Dispose() }
            }
            $image.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
            "screenshot: $outPath, $($image.Width)x$($image.Height) of the $what; origin $($bounds.X),$($bounds.Y) scale $Scale (screen = origin + pixel / scale)"
            if ($image -ne $bitmap) { $image.Dispose() }
        }
        catch { Stop-With "screenshot failed: $($_.Exception.Message)" }
        finally { $bitmap.Dispose() }
    }
    'cursor' { Get-Point 'cursor:' }
    'move' {
        Assert-Bound 'X', 'Y'
        Assert-OnScreen $X $Y
        [FoGameControl.Native]::MoveTo($X, $Y)
        $p = [FoGameControl.Native]::Cursor()
        if ($p.X -ne $X -or $p.Y -ne $Y) { Stop-With "move to $X,$Y left the cursor at $($p.X),$($p.Y)" }
        "move: cursor at $X,$Y"
    }
    'move-relative' {
        Assert-Bound 'Dx', 'Dy'
        if ($Steps -lt 1) { Stop-With "-Steps must be at least 1, not $Steps" 2 }
        $sentX = 0; $sentY = 0
        for ($i = 1; $i -le $Steps; $i++) {
            $tx = [int][Math]::Round($Dx * $i / $Steps); $ty = [int][Math]::Round($Dy * $i / $Steps)
            [FoGameControl.Native]::MoveBy($tx - $sentX, $ty - $sentY)
            $sentX = $tx; $sentY = $ty
            Start-Sleep -Milliseconds 15
        }
        Get-Point "move-relative: sent $Dx,$Dy in $Steps steps;"
    }
    'hover' {
        Assert-Bound 'X', 'Y'
        Assert-OnScreen $X $Y
        # Approach from 24 px up-left in 1-pixel-diagonal input events, then nudge: one jump is a click target
        # but not a hover for Gameface (on Linux, absolute jumps never showed a tooltip).
        $startX = $X - 24; $startY = $Y - 24
        [FoGameControl.Native]::MoveTo($startX, $startY)
        for ($i = 1; $i -le 8; $i++) {
            Start-Sleep -Milliseconds 20
            [FoGameControl.Native]::MoveTo($startX + 3 * $i, $startY + 3 * $i)
        }
        foreach ($d in 1, -1) {
            Start-Sleep -Milliseconds 40
            [FoGameControl.Native]::MoveBy($d, 0)
        }
        Start-Sleep -Milliseconds 40
        [FoGameControl.Native]::MoveTo($X, $Y)
        Get-Point 'hover:'
    }
    'click' {
        Assert-Bound 'X', 'Y'
        Assert-OnScreen $X $Y
        [FoGameControl.Native]::MoveTo($X, $Y)
        Start-Sleep -Milliseconds 50
        $times = if ($Double) { 2 } else { 1 }
        for ($i = 0; $i -lt $times; $i++) {
            [FoGameControl.Native]::Button($Right.IsPresent, $false)
            Start-Sleep -Milliseconds $Hold
            [FoGameControl.Native]::Button($Right.IsPresent, $true)
            Start-Sleep -Milliseconds 60
        }
        $button = if ($Right) { 'right' } else { 'left' }
        "click: $button$(if ($Double) { ' double' }) at $X,$Y"
    }
    'key' {
        if (-not $Name) { Stop-With 'key needs a key name, e.g. key Escape' 2 }
        $codes = @(foreach ($part in $Name -split '\+') {
            $k = [System.Windows.Forms.Keys]::None
            if (-not [Enum]::TryParse([System.Windows.Forms.Keys], $part, $true, [ref]$k) -or $k -eq 'None' -or
                [int]$k -gt 0xFF) {
                Stop-With "unknown key '$part' (System.Windows.Forms.Keys names: Escape, Space, Enter, Tab, F1, Up, A, D1, ControlKey...)" 2
            }
            [int]$k
        })
        try {
            foreach ($c in $codes) { [FoGameControl.Native]::Key($c, $false); Start-Sleep -Milliseconds 20 }
            Start-Sleep -Milliseconds $Hold
            [array]::Reverse($codes)
            foreach ($c in $codes) { [FoGameControl.Native]::Key($c, $true); Start-Sleep -Milliseconds 20 }
        }
        catch { Stop-With "key failed: $($_.Exception.InnerException.Message ?? $_.Exception.Message)" }
        $front = [FoGameControl.Native]::Text([FoGameControl.Native]::GetForegroundWindow())
        "key: $Name sent to the foreground window '$front'"
    }
    'focus' {
        $h = Find-TargetWindow
        $text = [FoGameControl.Native]::Text($h)
        if (-not [FoGameControl.Native]::Focus($h)) {
            $front = [FoGameControl.Native]::Text([FoGameControl.Native]::GetForegroundWindow())
            Stop-With "couldn't bring '$text' to the front; '$front' kept it"
        }
        "focus: '$text' ($(Get-ProcessName $h)) is in front"
    }
    'window' {
        $h = Find-TargetWindow
        "window: '$([FoGameControl.Native]::Text($h))' ($(Get-ProcessName $h)) hwnd 0x$($h.ToString('X'))"
        "rect:   $(Format-Rect ([FoGameControl.Native]::Bounds($h)))"
        "client: $(Format-Rect ([FoGameControl.Native]::Client($h)))"
        "front:  $([FoGameControl.Native]::GetForegroundWindow() -eq $h)"
    }
    'windows' {
        foreach ($h in [FoGameControl.Native]::VisibleWindows()) {
            "'$([FoGameControl.Native]::Text($h))' ($(Get-ProcessName $h)) $(Format-Rect ([FoGameControl.Native]::Bounds($h)))"
        }
    }
    'wait-process' {
        if ($Timeout -lt 1) { Stop-With "-Timeout must be at least 1 second, not $Timeout" 2 }
        $name = if ($env:FO_GAME_PROCESS_NAME) { $env:FO_GAME_PROCESS_NAME } else { 'Cities2' }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($true) {
            $process = Get-Process -Name $name -ErrorAction SilentlyContinue
            if ($Gone -and -not $process) { "wait-process: $name gone after $([int]$clock.Elapsed.TotalSeconds) s"; break }
            $withWindow = @(Get-GameWindow $name) | Select-Object -First 1
            if (-not $Gone -and $withWindow) {
                "wait-process: $name window '$([FoGameControl.Native]::Text($withWindow))' up after $([int]$clock.Elapsed.TotalSeconds) s"
                break
            }
            if ($clock.Elapsed.TotalSeconds -ge $Timeout) {
                $state = if ($process) { 'running' } else { 'not running' }
                Stop-With "timed out after $Timeout s waiting for $name to $(if ($Gone) { 'exit' } else { 'show a window' }) ($state)"
            }
            Start-Sleep -Seconds 1
        }
    }
}
exit 0
