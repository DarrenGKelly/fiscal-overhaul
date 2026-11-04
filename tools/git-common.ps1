<#
    Shared git and path helpers for the PowerShell tools (land-pr, prune-worktrees, check-batch-status,
    new-test-batch, build, test). Dot-source it; it only defines functions. Windows only (Cities: Skylines II is),
    and Windows PowerShell 5.1 compatible (no ?? / ?: / ternaries, no Get-Date -AsUTC).
#>

# Paths compare case-insensitively, with '\' separators.
$RepoPathComparison = [StringComparison]::OrdinalIgnoreCase

function ConvertTo-NormalizedPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    return [IO.Path]::GetFullPath(($Path.Trim() -replace '/', '\')).TrimEnd('\')
}

function Test-SamePath([string]$A, [string]$B) {
    $left = ConvertTo-NormalizedPath $A
    $right = ConvertTo-NormalizedPath $B
    if (-not $left -or -not $right) { return $false }
    return [string]::Equals($left, $right, $RepoPathComparison)
}

# True when $Child is $Parent or lies under it.
function Test-PathUnder([string]$Child, [string]$Parent) {
    $childPath = (ConvertTo-NormalizedPath $Child) + '\'
    $parentPath = (ConvertTo-NormalizedPath $Parent) + '\'
    return $childPath.StartsWith($parentPath, $RepoPathComparison)
}

function Join-ProcessArguments([string[]]$Arguments) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument -eq '') { '""' }
        elseif ($argument -match '[\s"]') { '"' + ($argument -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
        else { $argument }
    }
    return ($quoted -join ' ')
}

# Runs git without PowerShell 5.1's stderr-to-ErrorRecord behaviour. Returns ExitCode, Text (stdout), Error (stderr).
function Invoke-Git {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'git'
    $startInfo.Arguments = Join-ProcessArguments (@('-C', $Path) + $Arguments)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdout = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    $stderr = $stderrTask.Result

    $result = [pscustomobject]@{
        ExitCode = $process.ExitCode
        Text     = $stdout.Trim()
        Error    = $stderr.Trim()
    }

    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed in ${Path}: $($result.Error)"
    }

    return $result
}

function Get-GitTopLevel([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return $null }
    $result = Invoke-Git -Path $Path -Arguments @('rev-parse', '--show-toplevel') -AllowFailure
    if ($result.ExitCode -ne 0) { return $null }
    return ConvertTo-NormalizedPath $result.Text
}

# CallerRoot: the worktree the path is in. CommonDir: the shared .git dir. MainCheckout: the checkout that owns it.
function Get-RepoContext([string]$Path) {
    $callerRoot = Get-GitTopLevel $Path
    if (-not $callerRoot) { throw "Not inside a git checkout: $Path" }

    $commonDir = ConvertTo-NormalizedPath (Invoke-Git -Path $callerRoot -Arguments @('rev-parse', '--path-format=absolute', '--git-common-dir')).Text

    return [pscustomobject]@{
        CallerRoot   = $callerRoot
        CommonDir    = $commonDir
        MainCheckout = ConvertTo-NormalizedPath (Split-Path -Parent $commonDir)
    }
}

# Null when git will run the repo's hooks; otherwise why it won't. A hooks path naming a missing folder makes git
# skip every hook silently (seen after a checkout was renamed), so sessions check it early.
function Get-HooksPathProblem([string]$Repo) {
    $top = Get-GitTopLevel $Repo
    if (-not $top) { return $null }
    $configured = Invoke-Git -Path $top -Arguments @('config', '--get', 'core.hooksPath') -AllowFailure
    if ($configured.ExitCode -ne 0 -or -not $configured.Text) {
        return "core.hooksPath isn't set, so git runs none of .githooks/ (git config core.hooksPath .githooks)"
    }

    $hooksPath = $configured.Text.Trim()
    if ($hooksPath.StartsWith('~')) { $hooksPath = $HOME + $hooksPath.Substring(1) }
    $resolved = if ([IO.Path]::IsPathRooted($hooksPath)) { $hooksPath } else { Join-Path $top $hooksPath }

    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        return "core.hooksPath is '$hooksPath' but that folder doesn't exist ($resolved)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'pre-commit'))) {
        return "core.hooksPath is '$hooksPath' but it has no pre-commit ($resolved)"
    }
    return $null
}

function Write-HooksPathWarning([string]$Repo) {
    $problem = Get-HooksPathProblem $Repo
    if (-not $problem) { return }
    Write-Host "WARNING: $problem. Git skips every hook (branch policy, doc checks) until it is fixed." -ForegroundColor Red
    Write-Host "         Fix: git config core.hooksPath .githooks" -ForegroundColor Red
}

function Get-UtcTimestamp([DateTime]$Date) {
    return $Date.ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture)
}

# The application, found on PATH or in its usual install folder; $null when missing.
function Find-Application([string]$Name, [string[]]$Fallbacks = @()) {
    $found = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { return $found.Source }
    foreach ($path in $Fallbacks) { if (Test-Path -LiteralPath $path) { return $path } }
    return $null
}

# The per-user toolchain under %LOCALAPPDATA%\fiscal-overhaul-tools (knowledge/testing.md#running-tests): puts the
# newest node-v*-win-x64 folder first on PATH, and with -Dotnet6 sets DOTNET_ROOT to its dotnet6 folder (the game's
# ModPostProcessor.exe needs the .NET 6 runtime; the mod build fails with "You must install or update .NET" without
# it). Either is left alone when its folder is missing. FO_TOOLS_ROOT overrides the folder (the tests use it).
function Use-LocalToolchain([switch]$Dotnet6) {
    $tools = if ($env:FO_TOOLS_ROOT) { $env:FO_TOOLS_ROOT } elseif ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'fiscal-overhaul-tools' } else { $null }
    if (-not $tools -or -not (Test-Path -LiteralPath $tools)) { return }
    $node = Get-ChildItem -LiteralPath $tools -Directory -Filter 'node-v*' -ErrorAction SilentlyContinue |
        Sort-Object { try { [version]($_.Name -replace '^node-v([\d.]+).*$', '$1') } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if ($node -and ($env:PATH -split [IO.Path]::PathSeparator) -notcontains $node.FullName) {
        $env:PATH = $node.FullName + [IO.Path]::PathSeparator + $env:PATH
    }
    $dotnet = Join-Path $tools 'dotnet6'
    if ($Dotnet6 -and (Test-Path -LiteralPath $dotnet)) { $env:DOTNET_ROOT = $dotnet }
}

# The running Cities: Skylines II process (Cities2.exe), or $null. Only one game instance can run, and every
# in-game test shares it (knowledge/orchestration.md#the-shared-game-instance).
function Get-GameProcess {
    return Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue | Select-Object -First 1
}
