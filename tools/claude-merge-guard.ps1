<#
.SYNOPSIS
    Claude Code PreToolUse guard: Claude lands PRs only through tools/land-pr.ps1 and never skips the git hooks.

.DESCRIPTION
    The git hooks can't see merges made through GitHub, and master may have no server-side protection (a private
    repo on the Free plan has none), so this is what stops a session (autonomous ones especially) from merging into master, or into
    autodev past land-pr's checks (base autodev, the merged tree's doc budgets).
    Called by tools/claude-merge-guard.sh with the hook payload on stdin. Exit 2 blocks the tool call and shows
    the reason to Claude; exit 0 allows it. Malformed payloads are allowed, so a bug here can't block every command.

    Blocks:
      - gh pr merge run as a command, from every session, the user's included: land with
        ./tools/land-pr.ps1 <N>, whose own gh pr merge is a child process this hook never sees; master PRs are
        merged on GitHub. Only command position counts (start of a line, after ; & | ( { or $( , after env
        assignments): the text inside quotes, heredoc bodies and here-strings is data (a grep pattern, a commit
        message), except a quoted script a shell runs (bash -c "...", pwsh -Command '...', "$( ... )").
      - gh api calls to merge endpoints (pulls/N/merge, /merges, GraphQL merge mutations)
      - skipping git hooks: --no-verify, git commit -n, pointing core.hooksPath anywhere but .githooks
      - deleting, renaming or rewriting branches or the repository on GitHub: gh api calls other than GET on git/refs,
        /branches/ or the repo itself; GraphQL deleteRef/updateRef(s)/archiveRepository/updateRepository;
        gh repo delete/archive/rename/edit
      - gh pr/issue create and gh pr/issue comment without --body-file (or -F): without a body gh stops at an interactive prompt,
        and an inline --body puts its text on the command line (a multi-line body breaks shell quoting easily)

    Every rule reads the command as a shell runs it (Get-CommandTexts): quoted text, heredoc bodies and here-strings
    are data, so a grep for any of these commands passes. gh api's rules then read the raw command, since a GraphQL
    mutation is quoted, and core.hooksPath's read the raw value.

    Warns (allows the command, and tells the session through the hook's JSON output):
      - setting a git-hook override (ALLOW_DOC_ERRORS, ALLOW_MASTER_COMMIT, ALLOW_MASTER_PUSH, ALLOW_AUTODEV_REWRITE),
        or land-pr.ps1 -SkipBudget, which are only for when the user asks. A warning, not a block, keeps the
        emergency path open (the user's call).

    Tests: ./tools/test-claude-merge-guard.ps1   (dot-sources this file; nothing below the functions runs then)
#>

$AutodevBranch = 'autodev'

# The text a shell runs as commands. Each quoted string becomes one placeholder word (Q), and heredoc and PowerShell
# here-string bodies drop out, so a grep pattern or a commit message can't look like a command. A quoted path to gh
# ("C:\Program Files\GitHub CLI\gh.exe") becomes the word gh, and a quoted variable ("$gh") stays itself, since the
# shell runs those. Nested holds the quoted strings a shell runs as a script: the argument after -c, -Command or /c,
# and a double-quoted string holding $( ... ). An unclosed quote masks to the end (allows: a bug here mustn't block
# every command).
function Get-CommandText([string]$Command) {
    $text = New-Object System.Text.StringBuilder
    $nested = New-Object System.Collections.Generic.List[string]
    $heredocs = New-Object System.Collections.Generic.List[string]
    $i = 0
    $n = $Command.Length
    while ($i -lt $n) {
        $c = $Command[$i]
        if ($c -eq "`n" -and $heredocs.Count -gt 0) {
            # The bodies of the heredocs this line opened, in order, each up to its delimiter line (tabs allowed: <<-).
            [void]$text.Append("`n")
            $i++
            foreach ($delimiter in $heredocs) {
                while ($i -lt $n) {
                    $end = $Command.IndexOf("`n", $i)
                    if ($end -lt 0) { $end = $n }
                    $line = $Command.Substring($i, $end - $i).TrimEnd("`r")
                    $i = [Math]::Min($end + 1, $n)
                    if ($line.TrimStart("`t") -eq $delimiter) { break }
                }
            }
            $heredocs.Clear()
            continue
        }
        if ($c -eq '<' -and $i + 1 -lt $n -and $Command[$i + 1] -eq '<') {
            if ($i + 2 -lt $n -and $Command[$i + 2] -eq '<') { [void]$text.Append('<<<'); $i += 3; continue }
            $m = [regex]::Match($Command.Substring($i), '^<<-?[ \t]*(?:''(?<d>[^''\r\n]+)''|"(?<d>[^"\r\n]+)"|\\?(?<d>[A-Za-z_][A-Za-z0-9_]*))')
            if ($m.Success) {
                $heredocs.Add($m.Groups['d'].Value)
                [void]$text.Append(' Q ')
                $i += $m.Length
                continue
            }
        }
        if ($c -eq '@' -and $i + 1 -lt $n -and ($Command[$i + 1] -eq '"' -or $Command[$i + 1] -eq "'")) {
            $quote = [regex]::Escape([string]$Command[$i + 1])
            $m = [regex]::Match($Command.Substring($i), '(?s)^@' + $quote + '[ \t]*\r?\n(?:.*?\r?\n)?' + $quote + '@')
            if ($m.Success) {
                [void]$text.Append(' Q ')
                $i += $m.Length
                continue
            }
        }
        if (($c -eq '\' -or $c -eq '`') -and $i + 1 -lt $n -and ($Command[$i + 1] -eq '"' -or $Command[$i + 1] -eq "'")) {
            [void]$text.Append('_')   # an escaped quote outside quotes (sh \" or PowerShell `") is a literal character
            $i += 2
            continue
        }
        if ($c -eq '"' -or $c -eq "'") {
            $j = $i + 1
            while ($j -lt $n -and $Command[$j] -ne $c) {
                if ($c -eq '"' -and ($Command[$j] -eq '\' -or $Command[$j] -eq '`') -and $j + 1 -lt $n) { $j++ }
                $j++
            }
            $content = $Command.Substring($i + 1, [Math]::Min($j, $n) - $i - 1)
            $tail = [Math]::Min($text.Length, 40)
            $before = $text.ToString($text.Length - $tail, $tail)
            if ($before -match '(?i)(?:^|\s)(?:-c|-Command|/c)\s*$' -or ($c -eq '"' -and $content.Contains('$('))) {
                $nested.Add($content)
            }
            if ($content -match '^(?:[^\r\n]*[\\/])?gh(?:\.exe)?$') { [void]$text.Append('gh') }
            elseif ($content -match '^\$\{?\w+\}?$') { [void]$text.Append($content) }
            else { [void]$text.Append('Q') }
            $i = $j + 1
            continue
        }
        [void]$text.Append($c)
        $i++
    }
    return [pscustomobject]@{ Text = $text.ToString(); Nested = $nested }
}

# gh pr merge at command position: a line start, after ; & | ( { or $( (so && || and PowerShell's & call operator too),
# past env assignments and env/command/exec/time/nohup/sudo. gh is bare, a path to it, or a variable holding one.
$DirectMergePattern = '(?m)(?:^|[;&|({]|\$\()\s*(?:(?:\w+=\S*|env|command|exec|time|nohup|sudo)\s+)*(?:(?:[^\s;&|(){}]*[\\/])?gh(?:\.exe)?|\$\{?\w+\}?)\s+pr\s+merge\b'

# The command's masked text (Get-CommandText) and that of each quoted script a shell runs in it, a few levels deep:
# what the rules read, so a grep pattern, commit message or heredoc that names a command can't trip them.
function Get-CommandTexts([string]$Command, [int]$Depth = 0) {
    $parsed = Get-CommandText $Command
    $texts = @($parsed.Text)
    if ($Depth -lt 3) {
        foreach ($inner in $parsed.Nested) { $texts += @(Get-CommandTexts $inner ($Depth + 1)) }
    }
    return $texts
}

# True when the command runs gh pr merge itself (quoted text, heredocs and here-strings excluded; quoted scripts a
# shell runs included, a few levels deep).
function Test-DirectMerge([string]$Command) {
    foreach ($text in Get-CommandTexts $Command) {
        if ($text -match $DirectMergePattern) { return $true }
    }
    return $false
}

# Returns $null to allow the command, or the reason it's blocked. The rules read the masked texts (Get-CommandTexts),
# except where they need what's quoted: gh api's rules read the raw command (a GraphQL mutation sits in a quoted
# query), but only once a masked text runs gh api; and core.hooksPath's read the raw value ("C:/x/.githooks" is fine).
function Get-MergeGuardVerdict([string]$Command) {
    if (-not $Command) { return $null }

    $gh = 'gh(?:\.exe)?["'']?\s+'
    $texts = @(Get-CommandTexts $Command)

    if (Test-DirectMerge $Command) {
        return "Don't run gh pr merge: land with ./tools/land-pr.ps1 <N> (the PR number or branch). land-pr checks the base is $AutodevBranch, the work is pushed and the merged tree fits its doc budgets, then merges. PRs into master are the user's, merged on GitHub."
    }

    $runsApi = [bool]($texts | Where-Object { $_ -match '(?:^|[^\w.-])(?:gh(?:\.exe)?|\$\{?\w+\}?)\s+api\b' })
    if ($runsApi -and $Command -match ($gh + 'api\b[^\r\n]*(pulls/[^/\s]+/merge\b|/merges\b|mergePullRequest|mergeBranch|enablePullRequestAutoMerge)')) {
        return "Merging through gh api is blocked. Land with ./tools/land-pr.ps1 <N> on a PR into $AutodevBranch."
    }

    # Branches and the repo itself, changed on GitHub where the git hooks can't see it. gh api sends -f/-F fields as a POST.
    foreach ($match in $(if ($runsApi) { [regex]::Matches($Command, $gh + 'api\b(?<args>[^\r\n;&|]*)') })) {
        $apiArgs = $match.Groups['args'].Value
        $method = 'GET'
        if ($apiArgs -match '(?:^|\s)(?:-X|--method)(?:\s*|=)["'']?(?<method>[A-Za-z]+)') { $method = $Matches['method'].ToUpperInvariant() }
        elseif ($apiArgs -match '(?:^|\s)(?:-[fF]|--field|--raw-field|--input)(?:\s|=|$)') { $method = 'POST' }
        $touchesBranches = $apiArgs -match 'git/refs|/branches/'
        $touchesRepo = $apiArgs -match '(?:^|[\s"''])/?repos/[^/\s"'']+/[^/\s"'']+/?["'']?(?=\s|$)'
        if ($method -ne 'GET' -and ($touchesBranches -or $touchesRepo)) {
            return "gh api $method on branches, refs or the repository is blocked. Branches change through git push (checked by the hooks) and PR merges; repository settings are the user's."
        }
    }
    if ($runsApi -and $Command -match ($gh + 'api\b[^\r\n]*\b(deleteRef|updateRefs?|archiveRepository|updateRepository)\b')) {
        return "GraphQL $($Matches[1]) is blocked. Branches change through git push (checked by the hooks) and PR merges; repository settings are the user's."
    }

    foreach ($text in $texts) {
        if ($text -match ($gh + 'repo\s+(delete|archive|rename|edit)\b')) {
            return "gh repo $($Matches[1]) is blocked; repository settings are the user's to change."
        }

        # Arguments run to the end of the command (quoted text is one placeholder word, so a title with '&' doesn't end it).
        foreach ($match in [regex]::Matches($text, $gh + '(?<kind>pr|issue)\s+(?<verb>create|comment)\b(?<args>[^\r\n;&|]*)')) {
            if ($match.Groups['args'].Value -notmatch '(?:^|\s)(?:--body-file|-F)(?:\s|=|$)') {
                $kind = $match.Groups['kind'].Value + ' ' + $match.Groups['verb'].Value
                return "gh $kind needs --body-file <file> (or -F): write the body to a file in the scratchpad and pass it. Without one gh stops at an interactive prompt, and an inline --body puts the text on the command line."
            }
        }

        if ($text -match '\bgit\b[^\r\n]*--no-verify\b') {
            return "Don't skip the git hooks (--no-verify). Fix what the hook reports instead."
        }

        # A '-n' inside the commit message is masked away, so only real flags count.
        foreach ($match in [regex]::Matches($text, '\bgit\b[^\r\n;&|]*?\bcommit\b(?<flags>[^\r\n;&|]*)')) {
            if ($match.Groups['flags'].Value -match '(^|\s)-[A-Za-z]*n[A-Za-z]*(?=\s|$)') {
                return "Don't skip the git hooks (git commit -n is --no-verify). Fix what the hook reports instead."
            }
        }
    }

    # Repointing core.hooksPath through git config or -c. A value ending in .githooks (relative or absolute) is the repo's own.
    $hooksValue = '(?:["''](?:[^"'']*[\\/])?\.githooks[\\/]?["'']|(?:[^\s"'';&|]*[\\/])?\.githooks[\\/]?["'']?)(?=[\s;&|]|$)'
    if ($Command -match ('-c\s*["'']?core\.hooksPath=(?!' + $hooksValue + ')') -or
        $Command -match ('\bgit\b[^\r\n;&|]*?\bconfig\b[^\r\n;&|"'']*?\score\.hooksPath\s+(?!' + $hooksValue + ')[^\s;&|-]') -or
        $Command -match '\bgit\b[^\r\n;&|]*?\bconfig\b[^\r\n;&|"'']*?--unset(?:-all)?\s+core\.hooksPath') {
        return "Don't point core.hooksPath away from .githooks; the branch policy lives there."
    }

    return $null
}

# The git-hook overrides. Setting one is allowed but warned about; only the user asks for them.
$HookOverrides = 'ALLOW_DOC_ERRORS', 'ALLOW_MASTER_COMMIT', 'ALLOW_MASTER_PUSH', 'ALLOW_AUTODEV_REWRITE'

# Returns $null, or a warning naming the overrides the command sets: VAR=1 (sh, also after export/env), $env:VAR = 1
# (PowerShell), set VAR=1 (cmd). Quoted text is skipped, so a commit message that only mentions one doesn't count.
function Get-MergeGuardWarning([string]$Command) {
    if (-not $Command) { return $null }
    $unquoted = [regex]::Replace($Command, '"[^"]*"|''[^'']*''', ' ')
    $names = $HookOverrides -join '|'
    $found = @([regex]::Matches($unquoted, '(?i)(?:^|[\s;&|(]|\$env:)(?<name>' + $names + ')\s*=') |
        ForEach-Object { $_.Groups['name'].Value } | Select-Object -Unique)
    # land-pr's -SkipBudget lands without the merged-tree doc budget check (a machine with no python).
    if ($unquoted -match '(?i)land-pr(?:\.ps1)?\b[^\r\n;&|]*\s-SkipBudget\b') { $found += '-SkipBudget (land-pr.ps1)' }
    if ($found.Count -eq 0) { return $null }
    return "This command sets $($found -join ', '), which overrides a git-hook or landing check. Overrides are only for when the user asked for this one; if they didn't, stop and fix what the check reports instead."
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        exit 0
    }
    if (-not $payload -or -not $payload.tool_input) { exit 0 }

    $verdict = Get-MergeGuardVerdict -Command $payload.tool_input.command
    if ($verdict) {
        [Console]::Error.WriteLine("claude-merge-guard: $verdict")
        exit 2
    }
    # A warning: exit 0 with JSON on stdout. additionalContext reaches the session; systemMessage shows the user.
    $warning = Get-MergeGuardWarning -Command $payload.tool_input.command
    if ($warning) {
        @{
            systemMessage      = "claude-merge-guard: $warning"
            hookSpecificOutput = @{ hookEventName = 'PreToolUse'; additionalContext = "claude-merge-guard warning: $warning" }
        } | ConvertTo-Json -Compress | Write-Output
    }
    exit 0
}
