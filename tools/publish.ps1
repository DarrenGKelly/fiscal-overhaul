<#
.SYNOPSIS
    Publishes a release of Fiscal Overhaul to Paradox Mods through the game's ModPublisher (the /publish skill).

.DESCRIPTION
    Builds a ref (default origin/master) in a clean, temporary detached worktree under TEMP, never this checkout,
    validates its Mod/Properties/PublishConfiguration.xml, and runs ModPublisher.exe the way the csiimod toolchain's
    publish profiles do (Mod.targets' ModPublisherConfig target): `ModPublisher.exe <Mode> <config> -c <content> -v`,
    from the worktree's Mod/ folder (ModPublisher resolves Thumbnail and Screenshot paths against its working folder).
    The mod is built with dotnet build -c Release into a temporary Mods folder (as tools/build.ps1 -Zip does), so the
    upload holds exactly the build and nothing a deploy left in the game's Mods folder (build-info.txt).

    Modes (-Mode; the default follows ModId in the config):
      Publish     a new mod; only while ModId is empty (the default then).
      NewVersion  a new version of the published mod; needs ModId (the default once it is set).
      Update      the mod's page only (descriptions, images, tags, access level); nothing is built or tagged.

    Refuses (exit 2), naming the fix, before anything is uploaded:
      - Cities: Skylines II is running (the game is one shared instance), or ModPublisher isn't installed;
      - the mode doesn't fit ModId (Publish with a ModId; NewVersion or Update without one);
      - ModVersion differs from UI/mod.json's version (or the csproj's Version/AssemblyVersion, if it sets one);
      - the ChangeLog is empty (NewVersion and Update: ModPublisher requires one);
      - the Thumbnail or any Screenshot file is missing; a template placeholder is left ("This is a long description",
        an empty Tag, ExternalLink, Screenshot, Dependency or RequiredDLC); a required field is empty; AccessLevel
        isn't Public, Private or Unlisted (ModPublisher would quietly make it Public);
      - a Screenshot whose path is the one the last released v* tag has but whose bytes changed (Paradox Mods caches
        pictures by name, so the page would keep the old one: a retaken picture needs a new name);
      - for a release (Publish or NewVersion without -AccessLevel Private), the version is already published: a
        v<ModVersion> git tag exists;
      - a private test (-AccessLevel Private) of a mod that has had a release (ModId set and any v* tag): the access
        level belongs to the whole mod, so it would hide the public mod.

    -DryRun does everything but run ModPublisher: validation, the Release build, and the exact command it would run.

    -AccessLevel Private is a test upload, which never uses up the release: from a temporary copy of the config
    (the tracked one is never changed) whose AccessLevel is Private and whose ModVersion carries a pre-release
    suffix, <ModVersion>-<TestSuffix> (default test.<MMddHHmm>, so 1.0.0-test.11042130; Paradox refuses a new version
    whose version string equals the current one, so each test needs its own). It is not tagged and needs no free
    tag, and the later public release of <ModVersion> is a NewVersion that also switches the page to the config's
    Public (every ModPublisher request carries the access level).

    After a successful release (Publish or NewVersion, not a private test) it tags the built commit v<ModVersion> and
    pushes the tag. ModPublisher
    never writes the new mod's id back into the config: it prints "Mod published with Id=<n>". So after a Publish
    this prints the id (or, failing that, any ModId the config was given back) and the follow-up: commit ModId to
    Mod/Properties/PublishConfiguration.xml through a PR into autodev.

    ModPublisher logs in with the Paradox account session the game stored (launch the game and log in once); this
    script never passes or asks for credentials. It runs with DOTNET_ROOT set to the per-user .NET 6
    (%LOCALAPPDATA%\fiscal-overhaul-tools\dotnet6) when that exists. Testing hooks: FO_MODPUBLISHER overrides the
    publisher (CSII_MODPUBLISHERPATH otherwise), FO_GAME_PROCESS_NAME the game's process name.

.EXAMPLE
    ./tools/publish.ps1 -DryRun                          # validate, build origin/master, print the command
    ./tools/publish.ps1                                  # upload origin/master (Publish or NewVersion by ModId)
    ./tools/publish.ps1 -AccessLevel Private -Ref origin/autodev   # a private test upload (1.0.0-test.<time>)
    ./tools/publish.ps1 -Mode Update                     # the page only

.NOTES
    Exit 0 = published (or a clean dry run); 1 = the build, ModPublisher or the tag push failed; 2 = refused.
    Releasing: knowledge/releasing.md. Tests: ./tools/test-publish.ps1
#>
param(
    [ValidateSet('Publish', 'NewVersion', 'Update')] [string]$Mode,
    [string]$Ref = 'origin/master',
    [switch]$DryRun,
    [ValidateSet('Private')] [string]$AccessLevel,
    [string]$TestSuffix
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-common.ps1')

function Write-Publish([string]$Message, [string]$Colour = 'Gray') { Write-Host "publish: $Message" -ForegroundColor $Colour }

$ConfigRelative = 'Mod\Properties\PublishConfiguration.xml'
$LongDescriptionPlaceholder = 'This is a long description'
# Files kept out of the upload (matched in the content folder, which is a temporary copy of the build: deleting is safe).
# Only Burst's debug symbols, FiscalOverhaul_win_x86_64.pdb: the Burst post-processor writes one per target and they are
# large and useless to players. KEPT on purpose: the managed FiscalOverhaul.pdb (gives line numbers in players' error
# logs) and FiscalOverhaul.mjs.LICENSE.txt (third-party licence notices the bundled UI must ship).
$UploadExcludePatterns = @('*_x86_64.pdb')
$ExternalLinkTypes = @('discord', 'github', 'youtube', 'twitch', 'x', 'paypal', 'patreon', 'buymeacoffee', 'kofi', 'crowdin', 'gitlab', 'gofundme')

# The config's elements (comments skipped). ModPublisher reads Value="..." or, for multi-line fields, the text.
function Get-ConfigElements([xml]$Document) { return @($Document.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }) }
function Get-ElementValue($Element) {
    if (-not $Element) { return $null }
    if ($Element.HasAttribute('Value')) { return $Element.GetAttribute('Value').Trim() }
    return $Element.InnerText.Trim()
}
function Get-ConfigValue($Elements, [string]$Name) { return Get-ElementValue ($Elements | Where-Object { $_.LocalName -eq $Name } | Select-Object -First 1) }

# Every reason the config at $Root can't be uploaded in $Mode, each naming its fix.
function Get-PublishProblems([string]$Root, $Elements, [string]$Mode, [string]$Level) {
    $problems = New-Object System.Collections.Generic.List[string]
    $fixOnAutodev = 'fix it on autodev and promote again (/promote)'
    $version = Get-ConfigValue $Elements 'ModVersion'
    if (-not $version) { $problems.Add("ModVersion is empty in $ConfigRelative`: /promote stamps it (python tools/promote.py stamp).") }
    else {
        $modJson = Join-Path $Root 'UI\mod.json'
        if (Test-Path -LiteralPath $modJson) {
            $uiVersion = (Get-Content -Raw -LiteralPath $modJson | ConvertFrom-Json).version
            if ($uiVersion -ne $version) { $problems.Add("ModVersion $version in $ConfigRelative doesn't match UI/mod.json's version $uiVersion`: /promote stamps both (python tools/promote.py stamp); $fixOnAutodev.") }
        }
        foreach ($project in @(Get-ChildItem -LiteralPath (Join-Path $Root 'Mod') -Filter '*.csproj' -ErrorAction SilentlyContinue)) {
            [xml]$csproj = Get-Content -Raw -LiteralPath $project.FullName
            foreach ($property in @($csproj.SelectNodes('//*[local-name()="Version" or local-name()="AssemblyVersion"]'))) {
                $assembly = $property.InnerText.Trim()
                if ($assembly -and $assembly -ne $version -and $assembly -ne "$version.0") {
                    $problems.Add("The assembly version $assembly ($($property.LocalName) in Mod/$($project.Name)) doesn't match ModVersion $version`: make them equal; $fixOnAutodev.")
                }
            }
        }
    }
    if ($Mode -ne 'Publish' -and -not (Get-ConfigValue $Elements 'ChangeLog')) {
        $problems.Add("ChangeLog is empty in $ConfigRelative`: a $Mode needs one. /promote stamps it from the round's PRs (python tools/promote.py stamp); $fixOnAutodev.")
    }
    foreach ($name in 'DisplayName', 'ShortDescription', 'LongDescription', 'GameVersion') {
        if (-not (Get-ConfigValue $Elements $name)) { $problems.Add("$name is empty in $ConfigRelative`: ModPublisher requires it; fill it in.") }
    }
    $long = Get-ConfigValue $Elements 'LongDescription'
    if ($long -and $long.Contains($LongDescriptionPlaceholder)) { $problems.Add("LongDescription still holds the template's '$LongDescriptionPlaceholder': write the mod's description.") }
    $thumbnail = Get-ConfigValue $Elements 'Thumbnail'
    if (-not $thumbnail) { $problems.Add("Thumbnail is empty in $ConfigRelative`: ModPublisher would upload its default picture. Point it at Properties/Thumbnail.png.") }
    elseif (-not (Test-Path -LiteralPath (Join-Path $Root "Mod\$thumbnail") -PathType Leaf)) { $problems.Add("Thumbnail file '$thumbnail' doesn't exist (paths are relative to Mod/): add it or fix the path.") }
    foreach ($element in $Elements) {
        $name = $element.LocalName
        $value = Get-ElementValue $element
        switch ($name) {
            'Screenshot' {
                if (-not $value) { $problems.Add("An empty <Screenshot Value=`"`" /> (a template placeholder) is in $ConfigRelative`: remove it or name a picture.") }
                elseif (-not (Test-Path -LiteralPath (Join-Path $Root "Mod\$value") -PathType Leaf)) { $problems.Add("Screenshot file '$value' doesn't exist (paths are relative to Mod/): add it or fix the path.") }
            }
            'Tag' { if (-not $value) { $problems.Add("An empty <Tag Value=`"`" /> (a template placeholder) is in $ConfigRelative`: remove it or fill it in.") } }
            'Dependency' { if (-not $element.GetAttribute('Id').Trim()) { $problems.Add("An empty <Dependency Id=`"`" /> (a template placeholder) is in $ConfigRelative`: remove it.") } }
            'RequiredDLC' { if (-not $value) { $problems.Add("An empty <RequiredDLC Value=`"`" /> (a template placeholder) is in $ConfigRelative`: remove it.") } }
            'ExternalLink' {
                $type = $element.GetAttribute('Type').Trim()
                $url = $element.GetAttribute('Url').Trim()
                if (-not $type -or -not $url) { $problems.Add("An ExternalLink with an empty Type or Url (a template placeholder) is in $ConfigRelative`: remove it or fill both in.") }
                elseif ($ExternalLinkTypes -notcontains $type.ToLowerInvariant()) { $problems.Add("ExternalLink type '$type' isn't one ModPublisher supports ($($ExternalLinkTypes -join ', ')): ModPublisher would drop it.") }
            }
        }
    }
    if (@('Public', 'Private', 'Unlisted') -cnotcontains $Level) { $problems.Add("AccessLevel '$Level' isn't Public, Private or Unlisted: ModPublisher would make the mod Public. Fix it in $ConfigRelative.") }
    return , $problems
}

# --- refusals that need no checkout ---
if (Get-GameProcess) {
    Write-Publish 'Cities: Skylines II is running: the game is one shared instance (knowledge/orchestration.md#the-shared-game-instance) and the build and upload must not race it. Close the game and run this again.' 'Red'
    exit 2
}
$publisher = $env:FO_MODPUBLISHER
if (-not $publisher) { $publisher = [Environment]::GetEnvironmentVariable('CSII_MODPUBLISHERPATH', 'User') }
if (-not $publisher) { $publisher = $env:CSII_MODPUBLISHERPATH }
if (-not $publisher -or -not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
    Write-Publish "ModPublisher not found ('$publisher'): install the modding toolchain in the game (Options > Modding), which sets CSII_MODPUBLISHERPATH, then open a new shell." 'Red'
    exit 2
}

# A private test upload: its own pre-release version string, no tag (see the help).
$isTest = [bool]$AccessLevel
if ($TestSuffix -and -not $isTest) { Write-Publish '-TestSuffix is for a private test upload: add -AccessLevel Private, or drop it for a release.' 'Red'; exit 2 }
if ($isTest -and -not $TestSuffix) { $TestSuffix = 'test.' + (Get-Date -Format 'MMddHHmm') }
if ($isTest -and ($TestSuffix -notmatch '^[0-9A-Za-z][0-9A-Za-z.-]*$' -or $TestSuffix.Length -gt 20)) {
    Write-Publish "-TestSuffix '$TestSuffix' isn't a short pre-release label (letters, digits, dots and hyphens, at most 20): use something like test.2." 'Red'
    exit 2
}

$context = Get-RepoContext $PSScriptRoot
$main = $context.MainCheckout
Invoke-Git -Path $main -Arguments @('fetch', '-q', '--tags', 'origin') | Out-Null
$sha = (Invoke-Git -Path $main -Arguments @('rev-parse', '--verify', '--quiet', "$Ref^{commit}") -AllowFailure).Text
if (-not $sha) { Write-Publish "Unknown ref '$Ref' (name it as origin/<branch>)." 'Red'; exit 2 }

$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
$worktree = Join-Path ([IO.Path]::GetTempPath()) "fo-publish-$id"
$package = Join-Path ([IO.Path]::GetTempPath()) "fo-publish-content-$id"
$savedUserData = $env:CSII_USERDATAPATH
# The checkout, validation, build, upload and tag; sets $script:exitCode and returns early on a refusal or failure.
function Invoke-Publish {
    Invoke-Git -Path $main -Arguments @('worktree', 'add', '-q', '--detach', $worktree, $sha) | Out-Null
    Write-Publish "Checked out $Ref ($($sha.Substring(0, 10))) in $worktree."
    $configPath = Join-Path $worktree $ConfigRelative
    if (-not (Test-Path -LiteralPath $configPath)) { Write-Publish "$Ref has no $ConfigRelative." 'Red'; $script:exitCode = 2; return }
    try { [xml]$config = Get-Content -Raw -LiteralPath $configPath }
    catch { Write-Publish "$ConfigRelative isn't valid XML: $($_.Exception.Message)" 'Red'; $script:exitCode = 2; return }
    $elements = Get-ConfigElements $config

    # --- the mode, from ModId ---
    $modId = Get-ConfigValue $elements 'ModId'
    if (-not $Mode) { $Mode = if ($modId) { 'NewVersion' } else { 'Publish' } }
    $problems = New-Object System.Collections.Generic.List[string]
    if ($modId -and $modId -notmatch '^\d+$') { $problems.Add("ModId '$modId' isn't a number: set it to the mod's Paradox Mods id.") }
    if ($Mode -eq 'Publish' -and $modId) { $problems.Add("ModId is $modId`: the mod is already on Paradox Mods, and Publish would make a second one. Publish a new version (-Mode NewVersion, the default) or update the page (-Mode Update).") }
    if ($Mode -ne 'Publish' -and -not $modId) { $problems.Add("ModId is empty: $Mode needs the published mod's id. Publish the mod first (no -Mode), or commit its ModId to $ConfigRelative through a PR into autodev.") }

    # --- the config ---
    $level = Get-ConfigValue $elements 'AccessLevel'
    if (-not $level) { $level = 'Public' }
    if ($AccessLevel) { $level = $AccessLevel }
    foreach ($problem in (Get-PublishProblems $worktree $elements $Mode $level)) { $problems.Add($problem) }
    $version = Get-ConfigValue $elements 'ModVersion'
    $tag = "v$version"
    $uploadVersion = if ($isTest) { "$version-$TestSuffix" } else { $version }
    if ($version -and $Mode -ne 'Update' -and -not $isTest) {
        $tagged = (Invoke-Git -Path $main -Arguments @('rev-parse', '--verify', '--quiet', "refs/tags/$tag^{commit}") -AllowFailure).Text
        if ($tagged) { $problems.Add("$tag is already published (tagged at $($tagged.Substring(0, 10))): bump the version (/promote) for a new release, or use -Mode Update for page-only changes. If that upload never happened, delete the tag (git tag -d $tag; git push origin :refs/tags/$tag).") }
    }
    # Paradox Mods caches page images by name: a screenshot replaced under the name the last release uploaded keeps
    # showing the old picture. So a changed picture needs a new name (tools/store-screenshots.ps1 suffixes the version).
    $lastRelease = @((Invoke-Git -Path $main -Arguments @('tag', '--list', 'v*', '--sort=-v:refname') -AllowFailure).Text -split "`n" | Where-Object { $_.Trim() }) | Select-Object -First 1
    if ($lastRelease) {
        $lastRelease = $lastRelease.Trim()
        foreach ($element in @($elements | Where-Object { $_.LocalName -eq 'Screenshot' })) {
            $value = Get-ElementValue $element
            if (-not $value) { continue }
            $path = 'Mod/' + ($value -replace '\\', '/')
            $released = (Invoke-Git -Path $main -Arguments @('rev-parse', '--verify', '--quiet', "$lastRelease`:$path") -AllowFailure).Text
            $current = (Invoke-Git -Path $main -Arguments @('rev-parse', '--verify', '--quiet', "$sha`:$path") -AllowFailure).Text
            if ($released -and $current -and $released -ne $current) {
                $problems.Add("Screenshot '$value' changed since $lastRelease under the same name: Paradox Mods caches pictures by name and would keep showing the old one. Give it a new name (tools/store-screenshots.ps1 adds the version: knowledge/releasing.md#screenshots); fix it on autodev and promote again (/promote).")
            }
        }
    }
    if ($isTest -and $modId) {
        $releases = (Invoke-Git -Path $main -Arguments @('tag', '--list', 'v*') -AllowFailure).Text
        if ($releases) { $problems.Add("A private test of mod $modId, which has had a release ($((($releases -split "`n") | Select-Object -Last 1).Trim()) and others): the access level is the whole mod's, so the upload would hide it from players. Test without uploading (-DryRun), or as a separate mod.") }
    }
    if ($problems.Count) {
        Write-Publish "Refused: $Ref isn't ready to $Mode." 'Red'
        foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Red }
        $script:exitCode = 2
        return
    }
    $kind = if ($isTest) { 'a private test upload: not tagged, the release stays unused' } else { 'a release' }
    Write-Publish "$Mode $(Get-ConfigValue $elements 'DisplayName') $uploadVersion (AccessLevel $level; $kind)."

    # --- the effective config: a temporary copy for a private test (its access level and version string) ---
    New-Item -ItemType Directory -Force (Join-Path $package 'Mods') | Out-Null
    $publishConfig = $configPath
    if ($isTest) {
        $copy = $config.Clone()
        foreach ($pair in @(@('AccessLevel', $AccessLevel), @('ModVersion', $uploadVersion))) {
            $node = $copy.DocumentElement.SelectSingleNode($pair[0])
            if (-not $node) { $node = $copy.DocumentElement.AppendChild($copy.CreateElement($pair[0])) }
            $node.SetAttribute('Value', $pair[1])
        }
        $publishConfig = Join-Path $package 'PublishConfiguration.xml'
        $copy.Save($publishConfig)
    }

    # --- the Release build, into a temporary Mods folder (Update uploads no content: nothing to build) ---
    $project = Get-ChildItem -LiteralPath (Join-Path $worktree 'Mod') -Filter '*.csproj' | Select-Object -First 1
    if (-not $project) { Write-Publish "$Ref has no Mod/*.csproj." 'Red'; $script:exitCode = 2; return }
    $content = Join-Path $package "Mods\$($project.BaseName)"
    Use-LocalToolchain -Dotnet6
    if ($Mode -ne 'Update') {
        $ui = Join-Path $worktree 'UI'
        $hasUI = Test-Path -LiteralPath (Join-Path $ui 'package.json')
        $ErrorActionPreference = 'Continue'
        try {
            if ($hasUI) {
                $npm = Find-Application 'npm.cmd' @('C:\Program Files\nodejs\npm.cmd')
                if (-not $npm) { Write-Publish 'npm not found (install Node.js LTS, or unpack it under %LOCALAPPDATA%\fiscal-overhaul-tools).' 'Red'; $script:exitCode = 1; return }
                $install = if (Test-Path -LiteralPath (Join-Path $ui 'package-lock.json')) { 'ci' } else { 'install' }
                Write-Publish "Installing the UI packages (npm $install)."
                Push-Location -LiteralPath $ui
                try { & $npm $install 2>&1 | ForEach-Object { Write-Host $_ }; $code = $LASTEXITCODE } finally { Pop-Location }
                if ($code -ne 0) { Write-Publish "npm $install failed (exit $code)." 'Red'; $script:exitCode = 1; return }
            }
            $env:CSII_USERDATAPATH = $package   # the UI's webpack build writes to <user data>\Mods\FiscalOverhaul
            $arguments = @('build', $project.FullName, '-c', 'Release', '-nologo', "-p:LocalModsPath=$(Join-Path $package 'Mods')")
            Write-Publish "Building the Release mod (dotnet $($arguments -join ' '))."
            Push-Location -LiteralPath $worktree
            try { & dotnet @arguments 2>&1 | ForEach-Object { Write-Host $_ }; $code = $LASTEXITCODE } finally { Pop-Location }
            if ($code -ne 0) { Write-Publish "The Release build failed (exit $code): nothing was uploaded." 'Red'; $script:exitCode = 1; return }
        }
        finally { $ErrorActionPreference = 'Stop' }
        $expected = @("$($project.BaseName).dll")
        if ($hasUI) { $expected += "$($project.BaseName).mjs" }
        $missing = @($expected | Where-Object { -not (Test-Path -LiteralPath (Join-Path $content $_)) })
        if ($missing.Count) { Write-Publish "The build left no $($missing -join ' or ') in $content`: nothing was uploaded." 'Red'; $script:exitCode = 1; return }
        foreach ($pattern in $UploadExcludePatterns) {
            foreach ($file in @(Get-ChildItem -LiteralPath $content -File -Filter $pattern)) {
                Remove-Item -LiteralPath $file.FullName -Force
                Write-Publish "Left out of the upload: $($file.Name) (Burst debug symbols)."
            }
        }
        Write-Publish "Built $content." 'Green'
    }
    else { New-Item -ItemType Directory -Force $content | Out-Null }

    # --- ModPublisher, as Mod.targets runs it ---
    $publishArguments = @($Mode, $publishConfig, '-c', $content, '-v')
    $modFolder = Join-Path $worktree 'Mod'
    $command = "& `"$publisher`" $(Join-ProcessArguments $publishArguments)"
    Write-Publish "Command (from $modFolder, DOTNET_ROOT=$env:DOTNET_ROOT):"
    Write-Host "  $command"
    if ($DryRun) {
        Write-Publish "Files that would be uploaded from ${content}:"
        foreach ($file in @(Get-ChildItem -LiteralPath $content -File -ErrorAction SilentlyContinue | Sort-Object Name)) { Write-Host "  $($file.Name)" }
        $after = if ($isTest) { ' (a private test: no tag)' } elseif ($Mode -ne 'Update') { ", then tag $tag" } else { '' }
        Write-Publish "Dry run: nothing uploaded. $Mode of $uploadVersion from $Ref ($($sha.Substring(0, 10)))$after." 'Green'
        return
    }

    $lines = New-Object System.Collections.Generic.List[string]
    Push-Location -LiteralPath $modFolder
    $ErrorActionPreference = 'Continue'
    try { & $publisher @publishArguments 2>&1 | ForEach-Object { $line = "$_"; Write-Host $line; $lines.Add($line) }; $code = $LASTEXITCODE }
    finally { Pop-Location; $ErrorActionPreference = 'Stop' }
    if ($code -ne 0) {
        Write-Publish "ModPublisher failed (exit $code): nothing was tagged. If it couldn't log in, launch the game, log in to your Paradox account, close it and run this again." 'Red'
        $script:exitCode = 1
        return
    }
    Write-Publish "ModPublisher: $Mode done; uploaded version string $uploadVersion, AccessLevel $level." 'Green'

    # --- the new mod's id ---
    if ($Mode -eq 'Publish') {
        $newId = $null
        foreach ($line in $lines) { if ($line -match 'Mod published with Id=(\d+)') { $newId = $Matches[1] } }
        if (-not $newId) {
            [xml]$after = Get-Content -Raw -LiteralPath $publishConfig
            $written = Get-ConfigValue (Get-ConfigElements $after) 'ModId'
            if ($written -match '^\d+$') { $newId = $written }
        }
        if ($newId) {
            Write-Publish "New ModId: $newId" 'Green'
            Write-Host "  Follow-up: commit it through a PR into autodev: on a branch from origin/autodev set <ModId Value=`"$newId`" /> in $ConfigRelative, then land it (./tools/land-pr.ps1). Later uploads, the public release after a private test included, are NewVersion."
        }
        else { Write-Publish "ModPublisher printed no ModId: find it in the mod's Paradox Mods page address, then commit it to $ConfigRelative through a PR into autodev." 'Yellow' }
    }

    # --- the tag (a release only: a private test leaves the version free) ---
    if ($isTest) { Write-Publish "A private test upload: not tagged; $version is still unreleased." 'Green' }
    elseif ($Mode -ne 'Update') {
        $message = "Fiscal Overhaul $version on Paradox Mods ($Mode, AccessLevel $level)"
        $made = Invoke-Git -Path $main -Arguments @('tag', '-a', $tag, $sha, '-m', $message) -AllowFailure
        $pushed = if ($made.ExitCode -eq 0) { Invoke-Git -Path $main -Arguments @('push', '-q', 'origin', "refs/tags/$tag") -AllowFailure } else { $made }
        if ($pushed.ExitCode -ne 0) {
            Write-Publish "Uploaded, but tagging $tag failed: $($pushed.Error). Tag it by hand: git tag -a $tag $sha -m `"$message`"; git push origin $tag" 'Red'
            $script:exitCode = 1
            return
        }
        Write-Publish "Tagged $tag at $($sha.Substring(0, 10)) and pushed it." 'Green'
    }
}

$script:exitCode = 0
try { Invoke-Publish }
finally {
    $env:CSII_USERDATAPATH = $savedUserData
    if (Test-Path -LiteralPath $worktree) {
        & (Join-Path $PSScriptRoot 'remove-worktree.ps1') -Path $worktree -Force | Out-Null
        if (Test-Path -LiteralPath $worktree) { Write-Publish "Couldn't remove the temporary worktree $worktree`: ./tools/remove-worktree.ps1 -Path $worktree -Force" 'Yellow' }
    }
    Invoke-Git -Path $main -Arguments @('worktree', 'prune') -AllowFailure | Out-Null
    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force -ErrorAction SilentlyContinue }
}
exit $exitCode
