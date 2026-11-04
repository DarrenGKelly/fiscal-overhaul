"""Lints the Claude Code skills in .claude/skills/<name>/SKILL.md and the agents in .claude/agents/<name>.md.

A skill whose frontmatter doesn't parse loses its description silently: the harness falls back to the H1, and
natural-language requests stop routing to it. This catches that and the other ways a skill quietly breaks:
  * SKILL.md exists and opens with a frontmatter block that parses (tools/okf.py's YAML subset: an unquoted
    description containing ": " or " #" is refused; quote it);
  * `name` matches the folder name;
  * `description` is present, single-line, MIN_DESCRIPTION to MAX_DESCRIPTION characters (the trigger text; every
    session carries every description, so detail goes in the body), and a skill's names `/<name>`;
  * every repo script a skill or agent names (`tools/x.ps1|py|sh`, `.githooks/x`, `.claude/skills|agents/...`) exists,
    and every `-Flag` after a `tools/x.ps1` is a parameter of that script (a renamed tool or flag goes stale silently);
  * `gh issue|pr list` carries `--limit` (gh's default of 30 truncates a backlog silently);
  * every relative link in the skill's Markdown files resolves (tools/check-doc-links.py's rules);
  * every Markdown file fits its size budget (tools/okf.py: SKILL_BUDGET, or its BUDGET_ALLOWLIST cap).
An agent definition gets the same frontmatter, link and budget checks (`name` matches the file name), and must name
its `model`: without one it inherits the spawning session's (Opus), which is how test agents ended up on Opus.

    python tools/check-skills.py              (every skill; exit 1 = problems)
    python tools/check-skills.py build        (named skills, or paths to skill folders)
Tests: python tools/test-check-skills.py
"""

import importlib.util
import os
import re
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SKILLS = ".claude/skills"
AGENTS = ".claude/agents"
MIN_DESCRIPTION = 100
MAX_DESCRIPTION = 450
AGENT_MODELS = ("sonnet", "opus", "haiku", "fable", "inherit")


def _load(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


okf = _load("okf", "okf.py")
doc_links = _load("check_doc_links", "check-doc-links.py")


def check_frontmatter(path, name, label, owner):
    """(frontmatter or None, problems) for a skill's SKILL.md or an agent's .md."""
    problems = []
    try:
        fm, _ = okf.load(path)
    except okf.FrontmatterError as error:
        fm = None
        problems.append("frontmatter doesn't parse, so it falls back to its H1 and won't trigger: %s" % error)
    else:
        if fm is None:
            problems.append("%s has no frontmatter (--- name / description ---)" % label)
    if fm is not None:
        if fm.get("name") != name:
            problems.append("name %r doesn't match %s %r" % (fm.get("name"), owner, name))
        description = fm.get("description")
        if not isinstance(description, str) or not description.strip():
            problems.append("description is missing or empty")
        elif "\n" in description:
            problems.append("description must be a single line")
        elif len(description.strip()) < MIN_DESCRIPTION:
            problems.append("description is %d chars; a trigger description needs at least %d (what it does and "
                            "when to use it)" % (len(description.strip()), MIN_DESCRIPTION))
        elif len(description.strip()) > MAX_DESCRIPTION:
            problems.append("description is %d chars; every session loads it, so keep it to %d (what it does, the "
                            "trigger phrasings, its callers; detail goes in the body)"
                            % (len(description.strip()), MAX_DESCRIPTION))
    return fm, problems


SCRIPT_REF = re.compile(r"(?:(?<=\./)|(?<![\w/.<-]))((?:tools|\.githooks)/[\w./-]+\.(?:ps1|py|sh)|\.githooks/[\w-]+"
                        r"|\.claude/(?:skills|agents)/[\w./-]+\.(?:md|py))")
PS1_ARGS = re.compile(r"tools/([\w.-]+\.ps1)((?:[ \t]+(?:-[A-Za-z]\w*|\"[^\"]*\"|<[^>]*>|[\w./:,+*-]+))*)")
FLAG = re.compile(r"(?<![\w-])-([A-Za-z]\w*)")
GH_LIST = re.compile(r"\bgh (?:issue|pr) list\b[^`\n]*")


def ps1_params(path, cache):
    """The lower-cased parameter names of a PowerShell script's param() block, or None when it has none."""
    if path not in cache:
        with open(path, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
        block = re.search(r"(?ims)^\s*param\s*\((.*?)^\)", text)
        cache[path] = ({name.lower() for name in re.findall(r"\$(\w+)", block.group(1))} if block else None)
    return cache[path]


def check_references(path, repo, cache):
    """Problems for the scripts, flags and gh list commands one Markdown file names."""
    problems = []
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    for number, line in enumerate(lines, 1):
        for match in SCRIPT_REF.finditer(line):
            target = match.group(1)
            if not os.path.exists(os.path.join(repo, target)):
                problems.append("%d: names %s, which doesn't exist" % (number, target))
        for match in PS1_ARGS.finditer(line):
            script = os.path.join(repo, "tools", match.group(1))
            if not os.path.isfile(script):
                continue
            params = ps1_params(script, cache)
            if params is None:
                continue
            for flag in FLAG.findall(re.sub(r'"[^"]*"|<[^>]*>', "", match.group(2))):
                if flag.lower() not in params:
                    problems.append("%d: -%s isn't a parameter of tools/%s" % (number, flag, match.group(1)))
        for match in GH_LIST.finditer(line):
            if "--limit" not in match.group(0) and " -L " not in match.group(0):
                problems.append("%d: `%s` has no --limit; gh's default of 30 truncates silently"
                                % (number, match.group(0).strip()))
    return problems


def check_agent(path, repo=REPO):
    """Problems (strings) for one .claude/agents/<name>.md; empty when it's fine."""
    name = os.path.splitext(os.path.basename(path))[0]
    fm, problems = check_frontmatter(path, name, os.path.basename(path), "the file name")
    if fm is not None and fm.get("model") not in AGENT_MODELS:
        problems.append("model %r isn't one of %s; without one the agent inherits the spawning session's model"
                        % (fm.get("model"), ", ".join(AGENT_MODELS)))
    for line, target, reason in doc_links.check_file(path, repo, {}):
        problems.append("%d: broken link %s (%s)" % (line, target, reason))
    problems.extend(check_references(path, repo, {}))
    problems.extend(okf.check_budget(path, repo))
    return problems


def agent_files(repo=REPO):
    root = os.path.join(repo, AGENTS)
    if not os.path.isdir(root):
        return []
    return [os.path.join(root, n) for n in sorted(os.listdir(root)) if n.lower().endswith(".md")]


def check_skill(folder, repo=REPO):
    """Problems (strings) for one skill folder; empty when it's fine."""
    name = os.path.basename(os.path.normpath(folder))
    path = os.path.join(folder, "SKILL.md")
    if not os.path.isfile(path):
        return ["no SKILL.md"]
    fm, problems = check_frontmatter(path, name, "SKILL.md", "the folder")
    if fm is not None and isinstance(fm.get("description"), str) and "/" + name not in fm["description"]:
        problems.append("description doesn't name /%s, so a typed /%s may not route to it" % (name, name))
    cache, params = {}, {}
    for current, dirs, names in os.walk(folder):
        dirs.sort()
        for file_name in sorted(names):
            if not file_name.lower().endswith(".md"):
                continue
            md = os.path.join(current, file_name)
            relname = okf.rel(md, folder)
            for line, target, reason in doc_links.check_file(md, repo, cache):
                problems.append("%s:%d: broken link %s (%s)" % (relname, line, target, reason))
            for problem in check_references(md, repo, params):
                problems.append("%s:%s" % (relname, problem))
            for problem in okf.check_budget(md, repo):
                problems.append("%s: %s" % (relname, problem))
    return problems


def skill_folders(argv, repo=REPO):
    root = os.path.join(repo, SKILLS)
    if not argv:
        if not os.path.isdir(root):
            return []
        return [os.path.join(root, n) for n in sorted(os.listdir(root)) if os.path.isdir(os.path.join(root, n))]
    folders = []
    for arg in argv:
        for candidate in (os.path.join(repo, arg), os.path.join(root, arg)):
            if os.path.isdir(candidate):
                folders.append(os.path.normpath(candidate))
                break
        else:
            raise SystemExit("check-skills: no such skill: %s" % arg)
    return folders


def main(argv, repo=REPO, out=None):
    out = out or sys.stdout
    folders = skill_folders(argv, repo)
    total = 0
    for folder in folders:
        for problem in check_skill(folder, repo):
            total += 1
            out.write("%s/%s: %s\n" % (SKILLS, os.path.basename(folder), problem))
    agents = [] if argv else agent_files(repo)
    for path in agents:
        for problem in check_agent(path, repo):
            total += 1
            out.write("%s/%s: %s\n" % (AGENTS, os.path.basename(path), problem))
    out.write("%d problem(s) in %d skill(s) and %d agent(s) checked\n" % (total, len(folders), len(agents)))
    return 1 if total else 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main(sys.argv[1:]))
