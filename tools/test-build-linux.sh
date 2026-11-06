#!/bin/sh
# Tests tools/build-linux.sh against a throwaway origin and stub ssh, scp and ps (never the network, the Windows PC
# or the game). Run after changing it: sh tools/test-build-linux.sh   (exit 0 = all passed)

repo_root=$(cd "$(dirname "$0")/.." && pwd)
root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT
failures=0

# A throwaway origin with autodev and a fix branch, and a clone holding the script under test.
git init -q --bare "$root/origin.git"
git clone -q "$root/origin.git" "$root/work" 2>/dev/null
(
    cd "$root/work" || exit 1
    git config user.email test@example.com && git config user.name test
    mkdir tools && cp "$repo_root/tools/build-linux.sh" tools/ && git add . && git commit -qm one
    git push -q origin HEAD:autodev && git switch -q -c fix/thing && echo two >two.txt && git add . && git commit -qm two
    git push -q origin HEAD:fix/thing
) >/dev/null 2>&1
fix_sha=$(git -C "$root/work" rev-parse origin/fix/thing)

# The zip the remote build "packaged": FiscalOverhaul/ with the DLL, the UI module and build-info.txt.
mkdir -p "$root/zip/FiscalOverhaul"
for name in FiscalOverhaul.dll FiscalOverhaul.mjs; do echo new >"$root/zip/FiscalOverhaul/$name"; done
printf 'branch origin/fix/thing\ncommit %s\n' "$(printf %.10s "$fix_sha")" >"$root/zip/FiscalOverhaul/build-info.txt"
(cd "$root/zip" && zip -qr ../good.zip FiscalOverhaul && rm FiscalOverhaul/FiscalOverhaul.mjs && zip -qr ../no-ui.zip FiscalOverhaul)

# Stubs. ssh decodes the PowerShell it was sent into $root/sent.ps1 and plays the remote build per STUB_BUILD;
# scp records uploads and serves STUB_ZIP for downloads; ps lists Cities2.exe when STUB_GAME=1.
mkdir "$root/bin"
cat >"$root/bin/ssh" <<'EOF'
#!/bin/sh
echo ssh >>"$STUB_ROOT/calls"
for last; do :; done
printf '%s' "${last##* }" | base64 -d | iconv -f UTF-16LE >"$STUB_ROOT/sent.ps1"
[ "$STUB_GAME_AFTER_BUILD" = 1 ] && touch "$STUB_ROOT/game"
case $STUB_BUILD in
    ok) printf 'noise\r\nbuild: Building it.\r\nbuild: Packaged C:\\Dev\\fo\\Builds\\FiscalOverhaul-0.1.0-abc.zip (x at abc).\r\n'; exit 0 ;;
    nozip) echo 'build: Compile check passed.'; exit 0 ;;
    *) echo 'error CS1002: ; expected'; echo 'build: Building the C# mod failed (exit 1).'; exit 1 ;;
esac
EOF
cat >"$root/bin/scp" <<'EOF'
#!/bin/sh
echo "scp $*" >>"$STUB_ROOT/calls"
for last; do :; done
case $last in
    *:*) exit 0 ;;
    *) cp "$STUB_ZIP" "$last" ;;
esac
EOF
cat >"$root/bin/ps" <<'EOF'
#!/bin/sh
echo bash
if [ -e "$STUB_ROOT/game" ]; then printf '%s\n' 'Z:\steamapps\common\Cities Skylines II\Cities2.exe'; fi
EOF
chmod +x "$root/bin/"*

# run <args...>: runs the script with a fresh Mods folder holding an old install; sets $code.
run() {
    rm -rf "$root/mods" "$root/calls" "$root/sent.ps1" "$root/game"
    mkdir -p "$root/mods/FiscalOverhaul" && echo old >"$root/mods/FiscalOverhaul/stale.txt"
    [ "$STUB_GAME" = 1 ] && touch "$root/game"
    PATH="$root/bin:$PATH" STUB_ROOT=$root FO_LINUX_MODS="$root/mods" FO_WINDOWS_HOST=winbox \
        sh "$root/work/tools/build-linux.sh" "$@" >"$root/out" 2>&1
    code=$?
}

check() {
    if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; sed 's/^/     | /' "$root/out"; failures=$((failures + 1)); fi
}
untouched='[ -f "$root/mods/FiscalOverhaul/stale.txt" ] && [ ! -f "$root/mods/FiscalOverhaul/FiscalOverhaul.mjs" ]'

export STUB_BUILD=ok STUB_ZIP="$root/good.zip" STUB_GAME=0 STUB_GAME_AFTER_BUILD=0
run --ref origin/fix/thing --configuration Debug
check 'installs the built zip over the old mod' '[ $code -eq 0 ] && [ -f "$root/mods/FiscalOverhaul/FiscalOverhaul.mjs" ] && [ ! -f "$root/mods/FiscalOverhaul/stale.txt" ]'
check 'reports the install with the ref and its build-info' 'grep -q "^build: Installed to .*(origin/fix/thing at " "$root/out" && grep -q "branch origin/fix/thing" "$root/out"'
check 'builds the ref with -Zip and the configuration on Windows' 'grep -q "build.ps1 -Ref '"'"'origin/fix/thing'"'"' -Zip -Configuration Debug" "$root/sent.ps1"'
check 'points origin at the uploaded bundle only for that command' 'grep -q "GIT_CONFIG_KEY_0 = .url\." "$root/sent.ps1" && grep -q "insteadOf" "$root/sent.ps1" && grep -q "Remove-Item -LiteralPath \$bundle" "$root/sent.ps1"'
check 'reads the origin URL before the override takes effect' '[ "$(grep -n "git remote get-url origin" "$root/sent.ps1" | cut -d: -f1)" -lt "$(grep -n "GIT_CONFIG_COUNT" "$root/sent.ps1" | cut -d: -f1)" ]'
check 'uploads the bundle and downloads the zip with / separators' 'grep -q "^scp -q .*origin.bundle winbox:fo-origin-" "$root/calls" && grep -q "^scp -q winbox:C:/Dev/fo/Builds/FiscalOverhaul-0.1.0-abc.zip " "$root/calls"'
check 'shows the remote build lines, not its noise' 'grep -q "^build: Building it\.$" "$root/out" && ! grep -q noise "$root/out"'

run
check 'defaults to origin/autodev' '[ $code -eq 0 ] && grep -q "build.ps1 -Ref '"'"'origin/autodev'"'"' -Zip \*>&1" "$root/sent.ps1"'

STUB_GAME=1; run; STUB_GAME=0
check 'refuses while the game runs, before any ssh' '[ $code -eq 2 ] && grep -q "is running" "$root/out" && [ ! -e "$root/calls" ] && '"$untouched"

STUB_GAME_AFTER_BUILD=1; run; STUB_GAME_AFTER_BUILD=0
check 'refuses to install when the game started during the build' '[ $code -eq 2 ] && grep -q "is running" "$root/out" && '"$untouched"

run --ref origin/nope
check 'refuses an unknown ref' '[ $code -eq 2 ] && grep -q "Unknown ref" "$root/out" && [ ! -e "$root/calls" ] && '"$untouched"

run --configuration Fast
check 'refuses a bad configuration' '[ $code -eq 2 ] && [ ! -e "$root/calls" ]'

run --ref
check 'refuses an option without its value' '[ $code -eq 2 ] && grep -q "needs a value" "$root/out"'

STUB_BUILD=fail; run; STUB_BUILD=ok
check 'a failed remote build leaves the mod untouched' '[ $code -eq 1 ] && grep -q "error CS1002" "$root/out" && grep -q "failed (exit 1)" "$root/out" && '"$untouched"

STUB_BUILD=nozip; run; STUB_BUILD=ok
check 'a build that names no zip leaves the mod untouched' '[ $code -eq 1 ] && grep -q "named no zip" "$root/out" && '"$untouched"

STUB_ZIP="$root/no-ui.zip"; run; STUB_ZIP="$root/good.zip"
check 'a zip without the UI module leaves the mod untouched' '[ $code -eq 1 ] && grep -q "has no FiscalOverhaul/" "$root/out" && '"$untouched"

if [ "$failures" -eq 0 ]; then echo "All passed."; else echo "$failures failed."; fi
[ "$failures" -eq 0 ]
