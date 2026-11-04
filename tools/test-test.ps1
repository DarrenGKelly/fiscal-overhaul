<#
.SYNOPSIS
    Tests tools/test.ps1 against a throwaway checkout with stub dotnet and npm.

.DESCRIPTION
    Never runs a real build or test: tools/test.ps1 and tools/git-common.ps1 are copied into a folder under TEMP,
    and dotnet.cmd and npm.cmd stubs first on PATH record their arguments (and CI) and exit with the code in
    FO_STUB_EXIT. What must hold: missing parts are skipped with a note and exit 0, unless named with
    -CSharpOnly/-UIOnly (exit 1); a failing run exits 1; dotnet gets a temporary LocalModsPath (never the game's
    Mods folder) and -Filter; npm test runs with CI=true; a fresh checkout's packages are installed first; the newest per-user node
    (FO_TOOLS_ROOT standing in for %LOCALAPPDATA%\fiscal-overhaul-tools) comes first on PATH.
        ./tools/test-test.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'

$failures = 0
function Check([bool]$Condition, [string]$Description, [string]$Detail = '') {
    if ($Condition) { Write-Host "ok   $Description" }
    else {
        Write-Host "FAIL $Description" -ForegroundColor Red
        if ($Detail) { Write-Host ($Detail.Trim() -replace '(?m)^', '       ') }
        $script:failures++
    }
}

function Write-File([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-test-ps1-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$repo = Join-Path $root 'repo'
$bin = Join-Path $root 'bin'
$log = Join-Path $root 'calls.txt'
$shell = (Get-Process -Id $PID).Path

function Invoke-TestScript([string[]]$Arguments = @(), [int]$StubExit = 0) {
    Set-Content -LiteralPath $log -Value ''
    $env:FO_STUB_EXIT = "$StubExit"
    $global:LASTEXITCODE = 0
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\test.ps1') @Arguments 2>&1 | Out-String }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Calls = (Get-Content -Raw -LiteralPath $log) }
}

$savedPath = $env:PATH
try {
    New-Item -ItemType Directory -Force (Join-Path $repo 'tools'), $bin | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test.ps1'), (Join-Path $PSScriptRoot 'git-common.ps1') (Join-Path $repo 'tools')
    $env:FO_STUB_LOG = $log
    $env:FO_TOOLS_ROOT = Join-Path $root 'no-tools'   # never the real per-user node, which would shadow the stubs
    foreach ($name in 'dotnet', 'npm') {
        Write-File (Join-Path $bin "$name.cmd") "@echo $name %* CI=%CI%>> `"%FO_STUB_LOG%`"`r`n@exit /b %FO_STUB_EXIT%`r`n"
    }
    $env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH

    Write-Host '--- nothing to test yet ---'
    $r = Invoke-TestScript
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'No Tests/\*\.csproj yet' -and $r.Output -match 'no UI/package.json yet') 'with no Tests/ and no UI/ both parts are skipped with a note, exit 0' $r.Output
    Check ($r.Calls.Trim() -eq '') '... and nothing runs' $r.Calls
    $r = Invoke-TestScript @('-CSharpOnly')
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'skipped') 'fail case: -CSharpOnly with no Tests/ project exits 1' $r.Output
    $r = Invoke-TestScript @('-UIOnly')
    Check ($r.ExitCode -eq 1) 'fail case: -UIOnly with no UI/package.json exits 1' $r.Output
    $r = Invoke-TestScript @('-CSharpOnly', '-UIOnly')
    Check ($r.ExitCode -eq 2) 'both -CSharpOnly and -UIOnly is refused (exit 2)' $r.Output

    Write-Host '--- C# tests ---'
    Write-File (Join-Path $repo 'Tests\FiscalOverhaul.Tests.csproj') "<Project />`n"
    $r = Invoke-TestScript @('-CSharpOnly', '-Filter', 'FullyQualifiedName~Tax')
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'C#\s+passed') 'a passing dotnet test reports passed, exit 0' $r.Output
    Check ($r.Calls -match 'dotnet test .*FiscalOverhaul\.Tests\.csproj' -and $r.Calls -match '-p:LocalModsPath=\S*fo-test-mods-') '... run on the Tests project with a temporary LocalModsPath' $r.Calls
    Check ($r.Calls -match '--filter FullyQualifiedName~Tax') '... passing -Filter through' $r.Calls
    Check ($r.Calls -notmatch 'npm') '... and -CSharpOnly runs no npm' $r.Calls
    $r = Invoke-TestScript @('-CSharpOnly') 1
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'C#\s+FAILED') 'fail case: a failing dotnet test exits 1' $r.Output

    Write-Host '--- UI tests ---'
    Write-File (Join-Path $repo 'UI\package.json') '{ "scripts": { "build": "webpack" } }'
    $r = Invoke-TestScript @('-UIOnly')
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'no "test" script') 'fail case: -UIOnly with no "test" script exits 1, saying why' $r.Output
    Write-File (Join-Path $repo 'UI\package.json') '{ "scripts": { "build": "webpack", "test": "vitest" } }'
    Write-File (Join-Path $repo 'UI\package-lock.json') '{}'
    $r = Invoke-TestScript @('-UIOnly')
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'UI\s+passed') 'a passing npm test reports passed, exit 0' $r.Output
    Check ($r.Calls -match '(?m)^npm ci ' -and $r.Calls.IndexOf('npm ci') -lt $r.Calls.IndexOf('npm test')) '... installing the packages first in a checkout without node_modules' $r.Calls
    Check ($r.Calls -match 'npm test CI=true') '... and running vitest with CI=true (run once, no watch)' $r.Calls
    New-Item -ItemType Directory -Force (Join-Path $repo 'UI\node_modules') | Out-Null
    $r = Invoke-TestScript @('-UIOnly')
    Check ($r.Calls -notmatch 'npm ci') 'with node_modules present nothing is installed' $r.Calls
    $r = Invoke-TestScript @('-UIOnly') 1
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'UI\s+FAILED') 'fail case: a failing npm test exits 1' $r.Output

    Write-Host '--- the per-user toolchain ---'
    $env:FO_TOOLS_ROOT = Join-Path $root 'tools-root'
    foreach ($version in '9.0.0', '22.1.0', '10.2.0') {
        Write-File (Join-Path $env:FO_TOOLS_ROOT "node-v$version-win-x64/npm.cmd") "@echo npm-v$version %* CI=%CI%>> `"%FO_STUB_LOG%`"`r`n@exit /b 0`r`n"
    }
    $r = Invoke-TestScript @('-UIOnly')
    Check ($r.Calls -match 'npm-v22\.1\.0 test') 'the newest node-v* under the tools folder comes first on PATH (22.1.0 over 10.2.0 and 9.0.0)' $r.Calls
    $env:FO_TOOLS_ROOT = Join-Path $root 'no-tools'

    Write-Host '--- both ---'
    $r = Invoke-TestScript
    Check ($r.ExitCode -eq 0 -and $r.Calls -match 'dotnet test' -and $r.Calls -match 'npm test') 'with no flag both parts run' $r.Calls
}
finally {
    $env:PATH = $savedPath
    Remove-Item Env:FO_STUB_LOG, Env:FO_STUB_EXIT, Env:FO_TOOLS_ROOT -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All test.ps1 checks passed.' -ForegroundColor Green
exit 0
