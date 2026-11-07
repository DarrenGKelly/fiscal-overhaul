<#
.SYNOPSIS
    Tests tools/publish.ps1 against a throwaway repo, a stub ModPublisher and stub dotnet and npm.

.DESCRIPTION
    Never uploads, builds or touches the game: a bare origin and a clone under TEMP carry a fixture
    PublishConfiguration.xml; FO_MODPUBLISHER names a ModPublisher.cmd stub that records its arguments, working folder,
    DOTNET_ROOT, the config it was handed and the content folder, prints "Mod published with Id=<n>" on Publish and
    exits FO_STUB_PUBLISH_EXIT; dotnet.cmd and npm.cmd stubs first on PATH stand in for the build (dotnet writes the
    mod's dll and mjs where the build would). FO_GAME_PROCESS_NAME points the game check at a process that never runs,
    or at a copy of ping.exe renamed fo-fake-game, so a real running game changes nothing.
    What must hold, each with a pass and a fail case: every refusal (the game running, no ModPublisher, an unknown ref,
    the mode against ModId, the version against UI/mod.json and the csproj, the ChangeLog, the Thumbnail and
    Screenshots, a Screenshot changed under the name the last v* tag released, template placeholders, AccessLevel, an existing v<version> tag); the dry run builds but never uploads
    or tags; the mode follows ModId; a private test (-AccessLevel Private) uploads <version>-test.<n> and Private from a
    copy, never the tracked config, tags nothing and ignores the release tag, but is refused once the mod has had a
    release; the public release after it is a NewVersion of the plain version with AccessLevel Public; a successful
    release tags and pushes v<version> at the built commit and reports the new ModId (printed, or written back); a
    failed build or upload tags nothing; the temporary worktree is always removed.
        ./tools/test-publish.ps1     (exit 0 = all passed)
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

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
function Git([string]$Path, [string[]]$Arguments) { (Invoke-Git -Path $Path -Arguments $Arguments).Text }

$root = Join-Path ([IO.Path]::GetTempPath()) ('fo-test-publish-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$origin = Join-Path $root 'origin.git'
$repo = Join-Path $root 'repo'
$bin = Join-Path $root 'bin'
$log = Join-Path $root 'calls.txt'
$seen = Join-Path $root 'seen-config.xml'
$shell = (Get-Process -Id $PID).Path

$Template = @'
<Publish>
	<!--Id must be set in order to update existed mod.-->
	<ModId Value="{ModId}" />
	<DisplayName Value="Fiscal Overhaul" />
	<ShortDescription Value="Line-chart budget history and density-based taxes" />
	<LongDescription>
{Long}
	</LongDescription>
	<Thumbnail Value="Properties/Thumbnail.png" />
	<Screenshot Value="{Screenshot}" />
	<Tag Value="{Tag}" />
	<ModVersion Value="{Version}" />
	<GameVersion Value="1.6.*" />
	<ChangeLog>{ChangeLog}</ChangeLog>
	<!--<ChangeLog>
	</ChangeLog>-->
	<ExternalLink Type="{LinkType}" Url="{LinkUrl}" />
	<AccessLevel Value="{Access}" />
</Publish>
'@

# Commits a fixture config (and optionally UI/mod.json's version and a csproj body) and pushes it to origin/master.
function Set-Release([hashtable]$Fields = @{}, [string]$UIVersion, [string]$Project = '<Project />') {
    $values = @{ ModId = ''; Long = 'Readable economy panel.'; Screenshot = 'Properties/Screenshots/01-chart.png'; Tag = 'Code Mod'
        Version = '1.0.0'; ChangeLog = '- First release'; LinkType = 'github'; LinkUrl = 'https://github.com/example/fiscal-overhaul'; Access = 'Public' }
    foreach ($key in $Fields.Keys) { $values[$key] = $Fields[$key] }
    $text = $Template
    foreach ($key in $values.Keys) { $text = $text.Replace("{$key}", [string]$values[$key]) }
    if (-not $UIVersion) { $UIVersion = $values.Version }
    Write-File (Join-Path $repo 'Mod\Properties\PublishConfiguration.xml') $text
    Write-File (Join-Path $repo 'UI\mod.json') "{ `"id`": `"FiscalOverhaul`", `"version`": `"$UIVersion`" }"
    Write-File (Join-Path $repo 'Mod\FiscalOverhaul.csproj') $Project
    Git $repo @('add', '-A') | Out-Null
    Git $repo @('commit', '-q', '--allow-empty', '-m', 'fixture') | Out-Null
    Git $repo @('push', '-q', 'origin', 'HEAD:master') | Out-Null
    return Git $repo @('rev-parse', 'HEAD')
}

function Remove-Tags {
    foreach ($tag in @((Git $repo @('tag', '--list')) -split "`n" | Where-Object { $_ })) {
        Git $repo @('tag', '-d', $tag.Trim()) | Out-Null
        Git $repo @('push', '-q', 'origin', ":refs/tags/$($tag.Trim())") | Out-Null
    }
}

function Invoke-Publish([string[]]$Arguments = @(), [int]$PublishExit = 0, [string]$ModIdOut = '', [int]$BuildExit = 0) {
    Set-Content -LiteralPath $log -Value ''
    if (Test-Path -LiteralPath $seen) { Remove-Item -LiteralPath $seen }
    $env:FO_STUB_PUBLISH_EXIT = "$PublishExit"
    $env:FO_STUB_MODID = $ModIdOut
    $env:FO_STUB_BUILD_EXIT = "$BuildExit"
    $global:LASTEXITCODE = 0
    $ErrorActionPreference = 'Continue'
    # Run from outside the repo: cleanup must not depend on the caller's folder.
    Push-Location -LiteralPath $root
    try { $output = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\publish.ps1') @Arguments 2>&1 | Out-String }
    finally { Pop-Location; $ErrorActionPreference = 'Stop' }
    $seenText = if (Test-Path -LiteralPath $seen) { Get-Content -Raw -LiteralPath $seen } else { '' }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Calls = (Get-Content -Raw -LiteralPath $log); Seen = $seenText }
}

function Get-RemoteTag([string]$Tag) {
    $line = Git $repo @('ls-remote', 'origin', "refs/tags/$Tag^{}")
    if ($line) { return ($line -split '\s+')[0] }
    return $null
}

$savedPath = $env:PATH
$savedNames = 'FO_MODPUBLISHER', 'FO_GAME_PROCESS_NAME', 'FO_TOOLS_ROOT', 'FO_STUB_LOG', 'FO_STUB_SEEN', 'FO_STUB_PUBLISH_EXIT', 'FO_STUB_MODID', 'FO_STUB_BUILD_EXIT', 'FO_STUB_WRITEBACK'
$fakeGame = $null
try {
    New-Item -ItemType Directory -Force $root, $bin | Out-Null
    Git $root @('init', '-q', '--bare', '-b', 'master', $origin) | Out-Null
    Git $root @('clone', '-q', $origin, $repo) | Out-Null
    Git $repo @('config', 'user.email', 'test@example.com') | Out-Null
    Git $repo @('config', 'user.name', 'Test') | Out-Null
    Git $repo @('config', 'core.hooksPath', (Join-Path $root 'no-hooks')) | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $repo 'tools') | Out-Null
    foreach ($name in 'publish.ps1', 'git-common.ps1', 'remove-worktree.ps1') { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) (Join-Path $repo 'tools') }
    Write-File (Join-Path $repo 'Mod\Properties\Thumbnail.png') 'png'
    Write-File (Join-Path $repo 'Mod\Properties\Screenshots\01-chart.png') 'png'
    Write-File (Join-Path $repo 'UI\package.json') '{ "scripts": { "build": "webpack" } }'
    Write-File (Join-Path $repo 'UI\package-lock.json') '{}'
    Write-File (Join-Path $repo '.gitignore') "bin/`nobj/`nnode_modules/`n"

    $env:FO_STUB_LOG = $log
    $env:FO_STUB_SEEN = $seen
    $env:FO_GAME_PROCESS_NAME = 'fo-test-no-game-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $env:FO_TOOLS_ROOT = Join-Path $root 'tools-root'
    New-Item -ItemType Directory -Force (Join-Path $env:FO_TOOLS_ROOT 'dotnet6') | Out-Null
    $env:FO_MODPUBLISHER = Join-Path $bin 'ModPublisher.cmd'
    # The stub: %1 mode, %2 config, %3 -c, %4 content, %5 -v.
    Write-File $env:FO_MODPUBLISHER (@(
            '@echo publisher %*>> "%FO_STUB_LOG%"'
            '@echo cwd=%CD%>> "%FO_STUB_LOG%"'
            '@echo DOTNET_ROOT=%DOTNET_ROOT%>> "%FO_STUB_LOG%"'
            '@dir /b %4>> "%FO_STUB_LOG%" 2>nul'
            '@copy /y %2 "%FO_STUB_SEEN%" >nul'
            '@if not "%FO_STUB_WRITEBACK%"=="" copy /y "%FO_STUB_WRITEBACK%" %2 >nul'
            '@if "%1"=="Publish" if not "%FO_STUB_MODID%"=="" echo Mod published with Id=%FO_STUB_MODID%'
            '@exit /b %FO_STUB_PUBLISH_EXIT%') -join "`r`n")
    Write-File (Join-Path $bin 'dotnet.cmd') (@(
            '@echo dotnet %*>> "%FO_STUB_LOG%"'
            '@if "%FO_STUB_BUILD_EXIT%"=="1" exit /b 1'
            '@mkdir "%CSII_USERDATAPATH%\Mods\FiscalOverhaul" 2>nul'
            '@echo dll> "%CSII_USERDATAPATH%\Mods\FiscalOverhaul\FiscalOverhaul.dll"'
            '@echo mjs> "%CSII_USERDATAPATH%\Mods\FiscalOverhaul\FiscalOverhaul.mjs"'
            '@echo pdb> "%CSII_USERDATAPATH%\Mods\FiscalOverhaul\FiscalOverhaul.pdb"'
            '@echo lic> "%CSII_USERDATAPATH%\Mods\FiscalOverhaul\FiscalOverhaul.mjs.LICENSE.txt"'
            '@echo burst> "%CSII_USERDATAPATH%\Mods\FiscalOverhaul\FiscalOverhaul_win_x86_64.pdb"'
            '@echo burst> "%CSII_USERDATAPATH%\Mods\FiscalOverhaul\FiscalOverhaul_win_x86_64.dll"'
            '@exit /b 0') -join "`r`n")
    Write-File (Join-Path $bin 'npm.cmd') "@echo npm %*>> `"%FO_STUB_LOG%`"`r`n@exit /b 0`r`n"
    $env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH
    Set-Release | Out-Null

    Write-Host '--- refusals before any checkout ---'
    $fakeExe = Join-Path $root 'fo-fake-game.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') $fakeExe
    $fakeGame = Start-Process -FilePath $fakeExe -ArgumentList '-n', '60', '127.0.0.1' -WindowStyle Hidden -PassThru
    $env:FO_GAME_PROCESS_NAME = 'fo-fake-game'
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'is running') 'fail case: refused (exit 2) while the game runs' $r.Output
    Check ($r.Calls.Trim() -eq '') '... before building anything' $r.Calls
    Stop-Process -Id $fakeGame.Id -Force -ErrorAction SilentlyContinue
    $fakeGame = $null
    $env:FO_GAME_PROCESS_NAME = 'fo-test-no-game'
    $publisher = $env:FO_MODPUBLISHER
    $env:FO_MODPUBLISHER = Join-Path $root 'missing\ModPublisher.exe'
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'ModPublisher not found' -and $r.Output -match 'Options > Modding') 'fail case: no ModPublisher is refused, naming the fix' $r.Output
    $env:FO_MODPUBLISHER = $publisher
    $r = Invoke-Publish @('-DryRun', '-Ref', 'origin/no-such-branch')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "Unknown ref") 'fail case: an unknown ref is refused' $r.Output

    Write-Host '--- dry run of a new mod ---'
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'Dry run: nothing uploaded') 'a valid config with no ModId dry-runs clean (exit 0)' $r.Output
    Check ($r.Output -match 'ModPublisher\.cmd" Publish \S*PublishConfiguration\.xml -c \S*fo-publish-content-\S*\\Mods\\FiscalOverhaul -v') '... printing the Publish command with a temporary content folder' $r.Output
    Check ($r.Calls -match 'npm ci' -and $r.Calls -match 'dotnet build \S*FiscalOverhaul\.csproj -c Release -nologo -p:LocalModsPath=\S*fo-publish-content-') '... after installing the UI packages and a Release build into a temporary Mods folder' $r.Calls
    Check ($r.Calls -notmatch 'publisher') '... without running ModPublisher' $r.Calls
    Check ($r.Output -match 'Files that would be uploaded' -and $r.Output -match '(?m)^\s+FiscalOverhaul\.pdb' -and $r.Output -notmatch '(?m)^\s+FiscalOverhaul_win_x86_64\.pdb') '... printing the final file list, without the Burst pdb' $r.Output
    Check (-not (Get-RemoteTag 'v1.0.0') -and -not (Git $repo @('tag', '--list'))) '... or tagging' (Git $repo @('tag', '--list'))
    $worktrees = @((Git $repo @('worktree', 'list', '--porcelain')) -split "`n" | Where-Object { $_ -like 'worktree *' })
    Check ($worktrees.Count -eq 1) '... and the temporary worktree is removed' ($worktrees -join "`n")
    $content = if ($r.Output -match ' -c (\S+) -v') { $Matches[1] } else { '' }
    Check ($content -and -not (Test-Path -LiteralPath $content)) '... with its content folder' $content

    Write-Host '--- a refused config lists every problem ---'
    Set-Release @{ Long = 'This is a long description'; Tag = ''; LinkType = ''; LinkUrl = ''; Screenshot = 'Properties/Screenshots/09-missing.png'; Access = 'Everyone' } -UIVersion '0.9.0' | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'Refused') 'fail case: a config with template leftovers is refused (exit 2)' $r.Output
    Check ($r.Output -match "doesn't match UI/mod.json's version 0\.9\.0") '... ModVersion against UI/mod.json' $r.Output
    Check ($r.Output -match 'This is a long description') '... the long-description placeholder' $r.Output
    Check ($r.Output -match 'empty <Tag') '... an empty Tag' $r.Output
    Check ($r.Output -match 'ExternalLink with an empty Type or Url') '... an empty ExternalLink' $r.Output
    Check ($r.Output -match "Screenshot file 'Properties/Screenshots/09-missing\.png' doesn't exist") '... a missing Screenshot file' $r.Output
    Check ($r.Output -match "AccessLevel 'Everyone'") '... an AccessLevel ModPublisher would turn Public' $r.Output
    Check ($r.Calls -notmatch '(?m)^(dotnet|publisher) ') '... before building or uploading' $r.Calls
    Set-Release @{ Screenshot = '' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'empty <Screenshot') 'fail case: the template''s empty Screenshot is refused' $r.Output
    Remove-Item -LiteralPath (Join-Path $repo 'Mod\Properties\Thumbnail.png')
    Set-Release | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "Thumbnail file 'Properties/Thumbnail\.png' doesn't exist") 'fail case: a missing Thumbnail is refused' $r.Output
    Write-File (Join-Path $repo 'Mod\Properties\Thumbnail.png') 'png'
    Set-Release -Project '<Project><PropertyGroup><Version>0.9.0</Version></PropertyGroup></Project>' | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'assembly version 0\.9\.0') 'fail case: a csproj Version other than ModVersion is refused' $r.Output
    Set-Release -Project '<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>' | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 0) 'a csproj Version equal to ModVersion passes' $r.Output

    Write-Host '--- publishing a new mod ---'
    $sha = Set-Release
    $r = Invoke-Publish @() -ModIdOut '4242'
    Check ($r.ExitCode -eq 0) 'a new mod publishes (exit 0)' $r.Output
    Check ($r.Calls -match '(?m)^publisher Publish \S*PublishConfiguration\.xml -c \S*\\Mods\\FiscalOverhaul -v') '... running ModPublisher Publish <config> -c <content> -v' $r.Calls
    Check ($r.Calls -match '(?m)^cwd=\S*fo-publish-[0-9a-f]{8}\\Mod\s*$') '... from the worktree''s Mod folder (where the config''s relative paths resolve)' $r.Calls
    Check ($r.Calls -match ('(?m)^DOTNET_ROOT=' + [regex]::Escape((Join-Path $env:FO_TOOLS_ROOT 'dotnet6')))) '... with DOTNET_ROOT the per-user .NET 6' $r.Calls
    Check ($r.Calls -match '(?m)^FiscalOverhaul\.dll' -and $r.Calls -match '(?m)^FiscalOverhaul\.mjs' -and $r.Calls -notmatch 'build-info') '... uploading the build: the dll and the UI module, no build-info.txt' $r.Calls
    Check ($r.Calls -notmatch '(?m)^FiscalOverhaul_win_x86_64\.pdb' -and $r.Output -match 'Left out of the upload: FiscalOverhaul_win_x86_64\.pdb') 'the Burst debug symbols are left out of the upload (fail case: before the exclusion the listing held them)' $r.Calls
    Check ($r.Calls -match '(?m)^FiscalOverhaul\.pdb' -and $r.Calls -match '(?m)^FiscalOverhaul\.mjs\.LICENSE\.txt' -and $r.Calls -match '(?m)^FiscalOverhaul_win_x86_64\.dll') '... while the managed .pdb, the LICENSE.txt and the Burst dll stay' $r.Calls
    Check ($r.Output -match 'New ModId: 4242' -and $r.Output -match 'PR into autodev') '... reporting the printed ModId and the follow-up PR' $r.Output
    Check ((Get-RemoteTag 'v1.0.0') -eq $sha) '... tagging v1.0.0 at the built commit and pushing the tag' "$(Get-RemoteTag 'v1.0.0') vs $sha"
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'v1\.0\.0 is already published') 'fail case: the same version again is refused: its tag exists' $r.Output
    Remove-Tags
    $env:FO_STUB_WRITEBACK = Join-Path $root 'written-back.xml'
    Write-File $env:FO_STUB_WRITEBACK ($Template.Replace('{ModId}', '5151'))
    $r = Invoke-Publish @()
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'New ModId: 5151') 'a ModId written back into the config is reported when none is printed' $r.Output
    Remove-Item Env:FO_STUB_WRITEBACK
    Remove-Tags
    $r = Invoke-Publish @()
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'printed no ModId') 'fail case: no ModId anywhere says where to find it' $r.Output
    Remove-Tags

    Write-Host '--- mode selection ---'
    Set-Release @{ ModId = '4242' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -match 'ModPublisher\.cmd" NewVersion ') 'with a ModId the default is NewVersion' $r.Output
    $r = Invoke-Publish @('-DryRun', '-Mode', 'Publish')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'ModId is 4242' -and $r.Output -match 'second one') 'fail case: Publish with a ModId is refused' $r.Output
    Set-Release | Out-Null
    $r = Invoke-Publish @('-DryRun', '-Mode', 'NewVersion')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'ModId is empty: NewVersion needs') 'fail case: NewVersion without a ModId is refused' $r.Output
    $r = Invoke-Publish @('-DryRun', '-Mode', 'Update')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'ModId is empty: Update needs') 'fail case: Update without a ModId is refused' $r.Output
    Set-Release @{ ModId = '4242'; ChangeLog = '' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'ChangeLog is empty') 'fail case: a NewVersion with an empty ChangeLog is refused' $r.Output
    Set-Release @{ ModId = 'abc' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "ModId 'abc' isn't a number") 'fail case: a ModId that isn''t a number is refused' $r.Output

    Write-Host '--- a private test upload ---'
    Remove-Tags
    Set-Release | Out-Null
    $tracked = Get-Content -Raw -LiteralPath (Join-Path $repo 'Mod\Properties\PublishConfiguration.xml')
    $r = Invoke-Publish @('-AccessLevel', 'Private') -ModIdOut '4242'
    Check ($r.ExitCode -eq 0 -and $r.Calls -match '(?m)^publisher Publish ') 'a private test of a new mod publishes (exit 0)' $r.Output
    Check ($r.Seen -match '<AccessLevel Value="Private"') '... with AccessLevel Private' $r.Seen
    Check ($r.Seen -match '<ModVersion Value="1\.0\.0-test\.\d{8}"' -and $r.Output -match 'uploaded version string 1\.0\.0-test\.\d{8}') '... and a pre-release version string, 1.0.0-test.<MMddHHmm>, reported' $r.Output
    Check ($r.Calls -match 'publisher Publish \S*fo-publish-content-\S*\\PublishConfiguration\.xml') '... from a temporary copy of the config' $r.Calls
    $after = Get-Content -Raw -LiteralPath (Join-Path $repo 'Mod\Properties\PublishConfiguration.xml')
    $onOrigin = Git $repo @('show', 'origin/master:Mod/Properties/PublishConfiguration.xml')
    Check ($after -eq $tracked -and $onOrigin -match 'AccessLevel Value="Public"' -and $onOrigin -match 'ModVersion Value="1\.0\.0"' -and -not (Git $repo @('status', '--porcelain'))) '... leaving the tracked config (Public, 1.0.0) and the checkout clean' (Git $repo @('status', '--porcelain'))
    Check (-not (Git $repo @('tag', '--list')) -and -not (Get-RemoteTag 'v1.0.0') -and $r.Output -match 'not tagged') '... tagging nothing: the release stays unused' (Git $repo @('tag', '--list'))
    Check ($r.Output -match 'New ModId: 4242') '... and reporting the new ModId' $r.Output
    $r = Invoke-Publish @('-AccessLevel', 'Private', '-TestSuffix', 'test.7') -ModIdOut '4242'
    Check ($r.ExitCode -eq 0 -and $r.Seen -match '<ModVersion Value="1\.0\.0-test\.7"') '-TestSuffix names the pre-release label' $r.Seen
    $r = Invoke-Publish @('-DryRun', '-AccessLevel', 'Private', '-TestSuffix', 'a b')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "isn't a short pre-release label") 'fail case: a -TestSuffix with a space is refused' $r.Output
    $r = Invoke-Publish @('-DryRun', '-TestSuffix', 'test.1')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'add -AccessLevel Private') 'fail case: -TestSuffix without -AccessLevel Private is refused' $r.Output
    Git $repo @('tag', '-a', 'v1.0.0', 'HEAD', '-m', 'x') | Out-Null
    $r = Invoke-Publish @('-DryRun', '-AccessLevel', 'Private')
    Check ($r.ExitCode -eq 0 -and $r.Output -notmatch 'already published') 'a private test makes no "already published" claim from a v1.0.0 tag' $r.Output
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'v1\.0\.0 is already published') 'fail case: the same tag refuses a release' $r.Output
    Git $repo @('tag', '-d', 'v1.0.0') | Out-Null

    Write-Host '--- the public release after a private test ---'
    $sha = Set-Release @{ ModId = '4242' }
    $r = Invoke-Publish @()
    Check ($r.ExitCode -eq 0 -and $r.Calls -match '(?m)^publisher NewVersion \S*fo-publish-[0-9a-f]{8}\\Mod\\Properties\\PublishConfiguration\.xml') 'with the test''s ModId committed, the release is a NewVersion from the tracked config' $r.Calls
    Check ($r.Seen -match '<ModVersion Value="1\.0\.0" />' -and $r.Seen -match '<AccessLevel Value="Public" />') '... uploading 1.0.0 with AccessLevel Public (which switches the page to Public)' $r.Seen
    Check ((Get-RemoteTag 'v1.0.0') -eq $sha) '... and tags v1.0.0' "$(Get-RemoteTag 'v1.0.0')"
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'v1\.0\.0 is already published') 'fail case: a published version can''t go again as a NewVersion' $r.Output
    $r = Invoke-Publish @('-DryRun', '-AccessLevel', 'Private')
    Check ($r.ExitCode -eq 2 -and $r.Output -match 'hide it from players') 'fail case: a private test of a mod with a release is refused (it would hide the mod)' $r.Output

    Write-Host '--- a page update ---'
    $r = Invoke-Publish @('-Mode', 'Update')
    Check ($r.ExitCode -eq 0 -and $r.Calls -match '(?m)^publisher Update ') 'Update runs ModPublisher Update even though the version is tagged' $r.Output
    Check ($r.Calls -notmatch '(?m)^(dotnet|npm) ') '... without building' $r.Calls
    Check ((Git $repo @('tag', '--list')).Trim() -eq 'v1.0.0') '... and tags nothing' (Git $repo @('tag', '--list'))
    Check ($r.Seen -match 'AccessLevel Value="Public"') 'without -AccessLevel the config''s own level is used' $r.Seen

    Write-Host '--- a screenshot changed under its released name ---'
    $shot = Join-Path $repo 'Mod\Properties\Screenshots\01-chart.png'
    Write-File $shot 'png, retaken'
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "Screenshot 'Properties/Screenshots/01-chart\.png' changed since v1\.0\.0 under the same name") 'fail case: a screenshot whose bytes changed under the name v1.0.0 released is refused' $r.Output
    $r = Invoke-Publish @('-Mode', 'Update')
    Check ($r.ExitCode -eq 2 -and $r.Calls -notmatch 'publisher' -and $r.Output -match 'changed since v1\.0\.0') '... a page Update too, before ModPublisher runs' $r.Output
    Write-File $shot 'png'
    Write-File (Join-Path $repo 'Mod\Properties\Screenshots\01-chart-v1-0-1.png') 'png, retaken'
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More'; Screenshot = 'Properties/Screenshots/01-chart-v1-0-1.png' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -notmatch 'changed since') 'the retaken picture under a new name passes' $r.Output
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -notmatch 'changed since') 'the released name with its released bytes passes' $r.Output

    Write-Host '--- a page image over the size limit ---'
    $big = Join-Path $repo 'Mod\Properties\Screenshots\01-chart-v1-0-1.png'
    [System.IO.File]::WriteAllBytes($big, [byte[]]::new(2100001))
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More'; Screenshot = 'Properties/Screenshots/01-chart-v1-0-1.png' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "Screenshot 'Properties/Screenshots/01-chart-v1-0-1\.png' is 2100001 bytes, over Paradox Mods' 2\.1 MB image limit") 'fail case: a screenshot over 2.1 MB is refused' $r.Output
    $r = Invoke-Publish @('-Mode', 'Update')
    Check ($r.ExitCode -eq 2 -and $r.Calls -notmatch 'publisher') '... a page Update too, before ModPublisher runs' $r.Output
    [System.IO.File]::WriteAllBytes($big, [byte[]]::new(2100000))
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More'; Screenshot = 'Properties/Screenshots/01-chart-v1-0-1.png' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 0 -and $r.Output -notmatch 'image limit') 'a screenshot at exactly the limit passes' $r.Output
    $thumb = Join-Path $repo 'Mod\Properties\Thumbnail.png'
    [System.IO.File]::WriteAllBytes($thumb, [byte[]]::new(2100001))
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More'; Screenshot = 'Properties/Screenshots/01-chart-v1-0-1.png' } | Out-Null
    $r = Invoke-Publish @('-DryRun')
    Check ($r.ExitCode -eq 2 -and $r.Output -match "Thumbnail 'Properties/Thumbnail\.png' is 2100001 bytes") 'fail case: a thumbnail over 2.1 MB is refused' $r.Output
    Write-File $thumb 'png'
    Write-File $big 'png, retaken'
    Set-Release @{ ModId = '4242'; Version = '1.0.1'; ChangeLog = '- More' } | Out-Null

    Write-Host '--- failures tag nothing ---'
    Set-Release @{ ModId = '4242'; Version = '1.0.2'; ChangeLog = '- More' } | Out-Null
    $r = Invoke-Publish @() -PublishExit 1
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'ModPublisher failed' -and -not (Get-RemoteTag 'v1.0.2')) 'fail case: a failed upload exits 1 and tags nothing' $r.Output
    $r = Invoke-Publish @() -BuildExit 1
    Check ($r.ExitCode -eq 1 -and $r.Output -match 'Release build failed' -and $r.Calls -notmatch 'publisher') 'fail case: a failed build exits 1 before ModPublisher' $r.Output
    $r = Invoke-Publish @()
    Check ($r.ExitCode -eq 0 -and (Get-RemoteTag 'v1.0.2')) 'the same release then publishes once nothing fails' $r.Output
    $worktrees = @((Git $repo @('worktree', 'list', '--porcelain')) -split "`n" | Where-Object { $_ -like 'worktree *' })
    Check ($worktrees.Count -eq 1) 'every run removed its temporary worktree' ($worktrees -join "`n")
}
finally {
    if ($fakeGame) { Stop-Process -Id $fakeGame.Id -Force -ErrorAction SilentlyContinue }
    $env:PATH = $savedPath
    foreach ($name in $savedNames) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All publish.ps1 checks passed.' -ForegroundColor Green
exit 0
