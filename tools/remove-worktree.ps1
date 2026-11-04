<#
.SYNOPSIS
    Removes a git worktree, unlinking any folder links in it first.

.DESCRIPTION
    A worktree may hold links into the main checkout: a session that saves an npm install links UI/node_modules
    to the main checkout's (New-Item -ItemType Junction). A recursive delete that follows such a link wipes the
    main checkout's copy, so every junction or symlink up to two levels down is unlinked non-recursively before
    git removes the folder. Gitignored build output (bin/, obj/, UI/build/, a real node_modules/) goes with it.

    Refuses a worktree with uncommitted or untracked (not ignored) changes unless -Force, and anything that isn't
    a linked worktree of this repo (git refuses the main working tree).

.EXAMPLE
    ./tools/remove-worktree.ps1 -Path ".claude/worktrees/fix-chart-legend"

.NOTES
    Exit 0 = removed; otherwise git's exit code (or 1 for a refused dirty worktree).
    Tests: ./tools/test-remove-worktree.ps1
#>
param(
    [Parameter(Mandatory)] [string]$Path,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$worktree = (Resolve-Path -LiteralPath $Path).Path
# The application, not 'git': a caller's function named Git (test scripts have one) would otherwise shadow it.
$gitExe = 'git.exe'

# Refuse a dirty worktree before unlinking anything, so a refused removal leaves its links in place.
if (-not $Force) {
    $dirty = & $gitExe -C $worktree status --porcelain --untracked-files=normal
    if ($dirty) {
        Write-Host 'remove-worktree: uncommitted changes; use -Force'
        exit 1
    }
}

# Only a linked worktree of a repo has a .git file (the main working tree has a .git folder): leave anything else.
$dotGit = Get-Item -LiteralPath (Join-Path $worktree '.git') -Force -ErrorAction SilentlyContinue
if ($dotGit -and -not $dotGit.PSIsContainer) {
    foreach ($item in @(Get-ChildItem -LiteralPath $worktree -Force -ErrorAction SilentlyContinue) +
        @(Get-ChildItem -LiteralPath $worktree -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { -not $_.LinkType -and $_.Name -ne '.git' } |
            ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue })) {
        if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
            # Non-recursive Delete() removes only the link, never the target's contents.
            $item.Delete()
            Write-Host "remove-worktree: unlinked $($item.FullName.Substring($worktree.Length).TrimStart('\'))"
        }
    }
}

$gitArgs = @('worktree', 'remove', $worktree)
if ($Force) { $gitArgs += '--force' }
& $gitExe @gitArgs
exit $LASTEXITCODE
