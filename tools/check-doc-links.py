"""Checks every relative markdown link and #anchor in the AI docs, skills, agents and CLAUDE.md.

A link is broken when its file (URL-decoded, resolved against the linking file's folder, or the repo root
for a leading "/") doesn't exist in the checkout, or when its #anchor matches no heading in the target
Markdown file. Anchors use GitHub's heading slugs (lowercase, punctuation dropped, spaces to hyphens,
"-1", "-2" for repeats) plus explicit id="..."/name="..." anchors. External links (http:, mailto:, ...) and links
inside code are ignored.

With no arguments it also runs its guards on the repo:
- orphan tools: every file under tools/ (outside test-* scripts and fixtures/) is named in TOOLS_DOC
  (knowledge/repo-structure.md), a skill or agent under .claude/, or a hook (.githooks/, .claude/settings.json): by its
  path under tools/, its file name, or a `backticked` glob or template naming it (`build-*.ps1`, `<name>.ps1`).
- test twins: every top-level tools/ script (.py, .ps1, .sh; not test-*, not a *-common.ps1 helper) has a
  tools/test-<name>.* twin, or TESTED_BY names the test that covers it (and that test names it), or NO_TEST_TWIN lists
  it with a reason. NO_TEST_TWIN only shrinks: a listed script that gains a test fails until its row goes.
- folder map: every folder the first column of a knowledge/repo-structure.md table names (`Folder/`, at the top level
  of the cell) exists, resolved against its section's heading (## Mod/ -> Mod/...). Rows that say gitignored or
  "not yet" are skipped.

    python tools/check-doc-links.py                  (knowledge/, .claude/skills, .claude/agents, CLAUDE.md; exit 1 = broken)
    python tools/check-doc-links.py CLAUDE.md docs   (any files or folders)
Tests: python tools/test-check-doc-links.py
"""

import fnmatch
import os
import re
import sys
import urllib.parse

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_ROOTS = ["knowledge", ".claude/skills", ".claude/agents", "CLAUDE.md"]

FENCE = re.compile(r"^\s*(```|~~~)")
HEADING = re.compile(r"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$")
HTML_ANCHOR = re.compile(r"""<[^>]*\b(?:id|name)\s*=\s*["']([^"']+)["']""", re.IGNORECASE)
INLINE_CODE = re.compile(r"(`+)(.*?)\1")
SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*:")


def slugify(heading):
    """GitHub's heading anchor, before de-duplication."""
    text = INLINE_CODE.sub(lambda m: m.group(2), heading)
    text = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", text)  # [label](url) -> label
    text = re.sub(r"<[^>]+>", "", text)                       # inline HTML tags
    text = text.strip().lower()
    text = re.sub(r"[^\w\- ]", "", text)                      # drop punctuation, keep word chars, - and space
    return text.replace(" ", "-")


def strip_code(lines):
    """Yields (line number, line) outside fenced blocks, with inline code blanked out."""
    fence = None
    for number, line in enumerate(lines, 1):
        match = FENCE.match(line)
        if match:
            if fence is None:
                fence = match.group(1)
            elif match.group(1) == fence:
                fence = None
            continue
        if fence is None:
            yield number, INLINE_CODE.sub(lambda m: " " * len(m.group(0)), line)


def anchors(text):
    """Every anchor a Markdown file defines: heading slugs (GitHub de-duplication) and HTML ids."""
    found, counts, fence = set(), {}, None
    for line in text.splitlines():
        match = FENCE.match(line)
        if match:
            if fence is None:
                fence = match.group(1)
            elif match.group(1) == fence:
                fence = None
            continue
        if fence is not None:
            continue
        found.update(HTML_ANCHOR.findall(line))
        heading = HEADING.match(line)
        if heading:
            slug = slugify(heading.group(2))
            count = counts.get(slug, 0)
            counts[slug] = count + 1
            found.add(slug if count == 0 else "%s-%d" % (slug, count))
    return found


def links(line):
    """Link targets on a line: [text](target) and ![alt](target), with balanced parentheses and <...> targets."""
    targets = []
    for match in re.finditer(r"\]\(", line):
        start = match.end()
        if line[start:start + 1] == "<":
            end = line.find(">", start)
            if end != -1:
                targets.append(line[start + 1:end])
            continue
        depth, i = 1, start
        while i < len(line):
            if line[i] == "(":
                depth += 1
            elif line[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        if depth == 0:
            target = line[start:i].strip()
            target = re.sub(r"""\s+(".*"|'.*')$""", "", target)  # optional link title
            targets.append(target)
    return targets


def check_file(path, repo=REPO, cache=None):
    """Returns (line, target, reason) for every broken link in one Markdown file."""
    cache = {} if cache is None else cache
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    broken = []
    for number, line in strip_code(lines):
        for target in links(line):
            if not target or SCHEME.match(target):
                continue
            file_part, _, anchor = target.partition("#")
            file_part = urllib.parse.unquote(file_part)
            anchor = urllib.parse.unquote(anchor)
            if not file_part:
                resolved = path
            elif file_part.startswith("/"):
                resolved = os.path.join(repo, file_part.lstrip("/"))
            else:
                resolved = os.path.join(os.path.dirname(path), file_part)
            resolved = os.path.normpath(resolved)
            if not os.path.exists(resolved):
                broken.append((number, target, "no such file"))
                continue
            if not anchor or not resolved.lower().endswith(".md") or os.path.isdir(resolved):
                continue
            if resolved not in cache:
                with open(resolved, encoding="utf-8") as handle:
                    cache[resolved] = anchors(handle.read())
            if anchor.lower() not in cache[resolved] and anchor not in cache[resolved]:
                broken.append((number, target, "no heading #" + anchor))
    return broken


def markdown_files(paths, repo=REPO):
    for path in paths:
        full = os.path.join(repo, path)
        if os.path.isfile(full):
            yield full
        for folder, _, names in os.walk(full):
            for name in sorted(names):
                if name.lower().endswith(".md"):
                    yield os.path.join(folder, name)


# ---------------------------------------------------------------- guards: orphan tools, folder map

TOOLS = "tools"
TOOLS_DOC = "knowledge/repo-structure.md"
TOOL_NAMERS = [TOOLS_DOC, ".claude/skills", ".claude/agents", ".claude/settings.json", ".githooks"]
FOLDER_MAP = "knowledge/repo-structure.md"
BACKTICKED = re.compile(r"`([^`\n]+)`")
TWIN_EXTENSIONS = (".py", ".ps1", ".sh")
# Top-level tools/ scripts whose test isn't tools/test-<name>.*: the test that covers each (it must exist and name it).
TESTED_BY = {
    "claude-merge-guard.sh": "test-claude-merge-guard.ps1",
}
# Top-level tools/ scripts with no test at all, a ratchet: an unlisted one fails, and one that gains a test must leave
# the list, so it only shrinks. *-common.ps1 helpers are tested through the scripts that dot-source them.
NO_TEST_TWIN = {
    "build.ps1": "a thin wrapper over dotnet build and npm run build; exercised by every /build",
}


def tool_files(repo):
    """Repo-relative paths of the files under tools/ that must be documented (not tests, fixtures or range files)."""
    root = os.path.join(repo, TOOLS)
    found = []
    for folder, dirs, names in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d not in ("fixtures", "__pycache__") and not d.startswith("."))
        for name in sorted(names):
            if name.startswith("test-") or name.endswith(".pyc"):
                continue
            found.append(os.path.relpath(os.path.join(folder, name), repo).replace(os.sep, "/"))
    return found


def namer_texts(repo):
    """The text of every file that may name a tool: tools.md, skills, agents and hooks."""
    texts = []
    for entry in TOOL_NAMERS:
        full = os.path.join(repo, entry)
        paths = [full] if os.path.isfile(full) else [
            os.path.join(folder, name) for folder, _, names in os.walk(full) for name in names]
        for path in sorted(paths):
            try:
                with open(path, encoding="utf-8") as handle:
                    texts.append(handle.read())
            except (UnicodeDecodeError, OSError):
                continue
    return texts


def tool_globs(texts):
    """fnmatch patterns from backticked globs and <templates> (`build-*.ps1`, `new-<kind>.ps1`). A pattern needs three
    literal characters besides its extension, so `*.ps1` or `<name>.json` names nothing."""
    patterns = set()
    for text in texts:
        for token in BACKTICKED.findall(text):
            token = token.strip()
            if token.startswith("tools/"):
                token = token[len("tools/"):]
            if " " in token or ("*" not in token and "<" not in token):
                continue
            pattern = re.sub(r"<[^>]*>", "*", token)
            literal = re.sub(r"\.[A-Za-z0-9]+$", "", pattern).replace("*", "").replace("/", "")
            if len(literal) >= 3:
                patterns.add(pattern)
    return patterns


def named(path, texts, patterns):
    """True when a tool (repo-relative path) is named: its path under tools/ or file name, as a whole word."""
    sub = path[len(TOOLS) + 1:]
    base = os.path.basename(path)
    for candidate in {sub, base}:
        word = re.compile(r"(?<![\w.-])" + re.escape(candidate) + r"(?![\w-])")
        if any(word.search(text) for text in texts):
            return True
    return any(fnmatch.fnmatchcase(sub, p) or ("/" not in p and fnmatch.fnmatchcase(base, p)) for p in patterns)


def orphan_tools(repo=REPO):
    """Files under tools/ that nothing names: no row in TOOLS_DOC, no skill, agent or hook."""
    texts = namer_texts(repo)
    patterns = tool_globs(texts)
    return [path for path in tool_files(repo) if not named(path, texts, patterns)]


def twin_problems(repo=REPO):
    """Top-level tools/ scripts without a tools/test-<name>.* twin that neither TESTED_BY nor NO_TEST_TWIN accounts
    for, a TESTED_BY test that doesn't exist or name its script, and NO_TEST_TWIN rows that are stale."""
    root = os.path.join(repo, TOOLS)
    if not os.path.isdir(root):
        return []
    scripts = sorted(n for n in os.listdir(root) if os.path.isfile(os.path.join(root, n)) and n.endswith(TWIN_EXTENSIONS))
    twins = {os.path.splitext(n)[0][len("test-"):] for n in scripts if n.startswith("test-")}
    if not twins:
        # A checkout whose tools/ holds no tool test at all is a fixture with a few tool copies (test-git-hooks.sh's
        # doc-check repo), not the tools folder this rule governs: its rows would all read stale.
        return []
    problems = []
    for name in scripts:
        stem = os.path.splitext(name)[0]
        if name.startswith("test-") or stem.endswith("-common"):
            continue
        has_twin = stem in twins
        if name in NO_TEST_TWIN:
            if has_twin or name in TESTED_BY:
                problems.append("tools/%s has a test now: delete its NO_TEST_TWIN row in tools/check-doc-links.py "
                                "(the ratchet)" % name)
            continue
        if name in TESTED_BY:
            test = os.path.join(root, TESTED_BY[name])
            if not os.path.exists(test):
                problems.append("tools/%s: TESTED_BY names tools/%s, which doesn't exist" % (name, TESTED_BY[name]))
            else:
                with open(test, encoding="utf-8", errors="replace") as handle:
                    if name not in handle.read():
                        problems.append("tools/%s: TESTED_BY says tools/%s tests it, but that test never names it"
                                        % (name, TESTED_BY[name]))
            continue
        if not has_twin:
            problems.append("tools/%s has no test: add tools/test-%s.<py|ps1|sh> with a pass and a fail case (or name the "
                            "test that covers it in TESTED_BY in tools/check-doc-links.py)" % (name, stem))
    for name in sorted(NO_TEST_TWIN):
        if name not in scripts:
            problems.append("NO_TEST_TWIN lists tools/%s, which doesn't exist: delete the row" % name)
    return problems


def section_prefix(heading):
    """The folder a repo-structure.md section's rows live in: '## Mod/ (the C# mod)' -> 'Mod/'; '' for a heading that
    doesn't open with a folder path ('## Top level')."""
    match = re.match(r"`?([\w.-]+(?:/[\w.-]+)*/)`?(?:\s|$)", heading.strip())
    return match.group(1) if match else ""


def top_level_tokens(cell):
    """Backticked tokens in a table cell outside any (parentheses)."""
    depth, out, tokens = 0, [], []
    for part in re.split(r"(`[^`]*`)", cell):
        if part.startswith("`") and part.endswith("`") and len(part) > 1:
            if depth == 0:
                tokens.append(part[1:-1])
            continue
        depth = max(0, depth + part.count("(") - part.count(")"))
    return tokens


def missing_mapped_folders(repo=REPO):
    """Folders the first column of repo-structure.md's tables names that don't exist (rows saying gitignored or
    "not yet" skip)."""
    path = os.path.join(repo, FOLDER_MAP)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    prefix, missing = "", []
    for number, line in enumerate(lines, 1):
        heading = HEADING.match(line)
        if heading and len(heading.group(1)) == 2:
            prefix = section_prefix(heading.group(2))
            continue
        if not line.startswith("|") or "gitignored" in line.lower() or "not yet" in line.lower():
            continue
        cells = line.split("|")
        if len(cells) < 3:
            continue
        for token in top_level_tokens(cells[1]):
            if not token.endswith("/") or token.startswith("/") or re.search("[<*{\u2013]", token):
                continue
            folder = prefix + token
            if not os.path.isdir(os.path.join(repo, folder)):
                missing.append((number, folder))
    return missing


def run_guards(repo=REPO):
    """Prints each guard problem; returns how many there were."""
    problems = 0
    for path in orphan_tools(repo):
        problems += 1
        print("%s: not named in %s, a skill or a hook; add it to a row there (or delete it if it was a one-shot "
              "that has run)" % (path, TOOLS_DOC))
    for problem in twin_problems(repo):
        problems += 1
        print(problem)
    for number, folder in missing_mapped_folders(repo):
        problems += 1
        print("%s:%d: the folder map names %s, which doesn't exist; fix the row" % (FOLDER_MAP, number, folder))
    return problems


def main(argv, repo=REPO):
    roots = argv or DEFAULT_ROOTS
    cache, total, checked = {}, 0, 0
    for path in markdown_files(roots, repo):
        checked += 1
        for number, target, reason in check_file(path, repo, cache):
            total += 1
            print("%s:%d: %s (%s)" % (os.path.relpath(path, repo).replace(os.sep, "/"), number, target, reason))
    print("%d broken link(s) in %d file(s) checked" % (total, checked))
    guard_problems = 0 if argv else run_guards(repo)
    return 1 if total or guard_problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
