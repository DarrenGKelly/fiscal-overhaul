#!/bin/sh
# Claude Code PreToolUse hook for Bash and PowerShell calls (wired up in .claude/settings.json).
# Cheap pre-filter: only commands that could merge a PR, change branches or the repo on GitHub, skip the git hooks,
# or create or comment on a PR or issue (which needs --body-file),
# pay for starting PowerShell, which makes the actual decision (tools/claude-merge-guard.ps1). Exit 2 blocks the call;
# setting a git-hook override (ALLOW_*) or land-pr -SkipBudget is allowed, with a warning the PowerShell side prints as hook JSON.

input=$(cat)
case "$input" in
    *merge*|*commit*|*no-verify*|*hooksPath*|*' api '*|*' repo '*|*' create'*|*' comment'*|*ALLOW_*|*SkipBudget*) ;;
    *) exit 0 ;;
esac

# Windows PowerShell (Git Bash on Windows runs this); PowerShell 7 (pwsh) when there is none.
if command -v powershell.exe >/dev/null 2>&1; then
    printf '%s' "$input" | powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/claude-merge-guard.ps1"
else
    printf '%s' "$input" | pwsh -NoProfile -File "$(dirname "$0")/claude-merge-guard.ps1"
fi
