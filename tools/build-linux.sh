#!/bin/sh
# Builds a ref of Fiscal Overhaul on the Windows PC over SSH and installs it into this Linux PC's game (Proton).
#
# The mod only builds on Windows (the game's csiimod toolchain, Burst through the Unity editor), so on Linux this:
#   1. fetches origin here and bundles its branches (git bundle), because the Windows PC can't reach GitHub over
#      SSH: Git's credential manager and gh keep their logins in the desktop session's keyring;
#   2. copies the bundle over and runs tools/build.ps1 -Ref <ref> -Zip in the Windows main checkout, with the origin
#      URL rewritten to the bundle for that one command (GIT_CONFIG_* env vars: no config is written), so the build
#      worktree gets exactly the commits fetched here; the Windows checkout itself is never touched;
#   3. copies the zip back and replaces <Mods>/FiscalOverhaul with it.
# It refuses while the game runs here (it would load half a mod) and stamps nothing itself: build-info.txt comes from
# build.ps1 inside the zip. The zip also stays in the Windows main checkout's Builds/.
#
#   sh tools/build-linux.sh                              # origin/autodev
#   sh tools/build-linux.sh --ref origin/fix/<name>      # a branch (always origin/)
#   sh tools/build-linux.sh --configuration Debug
#
# Options (or env): --host (FO_WINDOWS_HOST, default fc-windows, an ~/.ssh/config host), --repo (FO_WINDOWS_REPO,
# default C:\Dev\fiscal-overhaul), --mods (FO_LINUX_MODS, default the Proton prefix's Mods folder under
# ~/.steam/steam/steamapps/compatdata/949230).
# Exit 0 = installed; 1 = the build, a copy or the zip failed (the installed mod is untouched); 2 = refused (the
# game is running, an unknown ref, a bad option).
# Tests: sh tools/test-build-linux.sh

repo_root=$(cd "$(dirname "$0")/.." && pwd)
ref=origin/autodev
configuration=
host=${FO_WINDOWS_HOST:-fc-windows}
windows_repo=${FO_WINDOWS_REPO:-'C:\Dev\fiscal-overhaul'}
user_data="$HOME/.steam/steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
mods=${FO_LINUX_MODS:-"$user_data/Mods"}

say() { printf 'build: %s\n' "$*"; }
fail() { code=$1; shift; say "$*" >&2; exit "$code"; }

while [ $# -gt 0 ]; do
    case $1 in --ref|--configuration|--host|--repo|--mods) [ $# -ge 2 ] && [ -n "$2" ] || fail 2 "$1 needs a value." ;; esac
    case $1 in
        --ref) ref=$2; shift 2 ;;
        --configuration) configuration=$2; shift 2 ;;
        --host) host=$2; shift 2 ;;
        --repo) windows_repo=$2; shift 2 ;;
        --mods) mods=$2; shift 2 ;;
        -h|--help) sed -n '2,/^# Tests:/s/^# \{0,1\}//p' "$0"; exit 0 ;;
        *) fail 2 "Unknown option '$1' (see sh tools/build-linux.sh --help)." ;;
    esac
done
case $configuration in ''|Debug|Release) ;; *) fail 2 "--configuration is Debug or Release, not '$configuration'." ;; esac
case $ref in *"'"*) fail 2 "A ref can't contain a quote: '$ref'." ;; esac

game_running() { ps -eo args 2>/dev/null | grep -i 'cities2\.exe' | grep -v grep >/dev/null; }
refuse_if_running() {
    game_running && fail 2 "Cities: Skylines II is running: it holds the mod's files. Close it and run this again."
    return 0
}
refuse_if_running
[ -d "$mods" ] || fail 1 "No Mods folder at $mods (start the game once, or pass --mods)."

git -C "$repo_root" fetch -q origin || fail 1 "git fetch origin failed."
sha=$(git -C "$repo_root" rev-parse --verify --quiet "$ref^{commit}") || fail 2 "Unknown ref '$ref' (name it as origin/<branch>)."
say "Building $ref ($(printf %.10s "$sha")) on $host."

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
# origin's branches as refs/heads/*, so the Windows fetch (+refs/heads/*:refs/remotes/origin/*) maps them as GitHub would.
git init -q --bare "$work/origin.git" &&
    git -C "$work/origin.git" fetch -q "$repo_root" 'refs/remotes/origin/*:refs/heads/*' &&
    { git -C "$work/origin.git" update-ref -d refs/heads/HEAD 2>/dev/null; true; } &&
    git -C "$work/origin.git" bundle create -q "$work/origin.bundle" --all 2>/dev/null ||
    fail 1 "Couldn't bundle origin's branches."
bundle_name="fo-origin-$$.bundle"
scp -q "$work/origin.bundle" "$host:$bundle_name" || fail 1 "Couldn't copy the bundle to $host (is it on and reachable over ssh?)."

configuration_arg=
[ -n "$configuration" ] && configuration_arg=" -Configuration $configuration"
script="\$ProgressPreference = 'SilentlyContinue'
\$bundle = Join-Path \$HOME '$bundle_name'
try {
    Set-Location -LiteralPath '$windows_repo'
    \$origin = (git remote get-url origin)
    if (-not \$origin) { throw 'no origin remote in $windows_repo' }
    \$env:GIT_CONFIG_KEY_0 = 'url.' + (\$bundle -replace '\\\\', '/') + '.insteadOf'
    \$env:GIT_CONFIG_VALUE_0 = \$origin
    \$env:GIT_CONFIG_COUNT = '1'
    & ./tools/build.ps1 -Ref '$ref' -Zip$configuration_arg *>&1 | ForEach-Object { \"\$_\" }
    \$code = \$LASTEXITCODE
}
catch { Write-Output \"build: \$_\"; \$code = 1 }
finally { Remove-Item -LiteralPath \$bundle -Force -ErrorAction SilentlyContinue }
exit \$code"
encoded=$(printf '%s' "$script" | iconv -t UTF-16LE | base64 | tr -d '\n')
ssh "$host" "powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded" >"$work/remote.log" 2>&1
remote_code=$?
grep -E '^build:|error CS' "$work/remote.log" | sed 's/\r$//'
[ "$remote_code" -eq 0 ] || fail 1 "The build on $host failed (exit $remote_code)."
zip_path=$(sed -n 's/\r$//; s/^build: Packaged \(.*\.zip\) (.*/\1/p' "$work/remote.log" | tail -n 1)
[ -n "$zip_path" ] || fail 1 "The build on $host named no zip."

scp -q "$host:$(printf '%s' "$zip_path" | tr '\\' '/')" "$work/build.zip" || fail 1 "Couldn't copy $zip_path back."
mkdir "$work/unzipped"
# Compress-Archive writes '\' separators: unzip warns (exit 1) and still makes the folders.
unzip -q "$work/build.zip" -d "$work/unzipped" 2>/dev/null
[ $? -le 1 ] || fail 1 "Couldn't unzip $zip_path."
built="$work/unzipped/FiscalOverhaul"
[ -f "$built/FiscalOverhaul.dll" ] && [ -f "$built/FiscalOverhaul.mjs" ] || fail 1 "The zip has no FiscalOverhaul/ with the DLL and the UI module."

refuse_if_running
rm -rf "$mods/FiscalOverhaul" && cp -R "$built" "$mods/FiscalOverhaul" || fail 1 "Couldn't install into $mods."
sed 's/^/  /' "$mods/FiscalOverhaul/build-info.txt" 2>/dev/null
say "Installed to $mods/FiscalOverhaul ($ref at $(printf %.10s "$sha")). The game loads it at its next start."
