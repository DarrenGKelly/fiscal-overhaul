"""Tests tools/check-skills.py against throwaway skill folders, then checks the real .claude/skills.

Each behaviour has a pass case (a skill the lint accepts) and a fail case (one it must report). Run after changing
check-skills.py:
    python tools/test-check-skills.py     (exit 0 = all passed)
"""

import importlib.util
import io
import os
import shutil
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("check_skills", os.path.join(HERE, "check-skills.py"))
check_skills = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check_skills)

failures = 0
GOOD_DESCRIPTION = ("Build a new widget from its design note to ready for polish. Use when the user says /widget, "
                    "asks for a widget, or a brief needs one.")


def check(condition, description, detail=""):
    global failures
    if condition:
        print("ok   " + description)
    else:
        print("FAIL " + description)
        if detail:
            print("       " + str(detail).replace("\n", "\n       "))
        failures += 1


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


root = tempfile.mkdtemp(prefix="check-skills-test-")
try:
    skills = os.path.join(root, ".claude", "skills")
    write(os.path.join(root, "knowledge", "units.md"), "# Units\n\n## Ship bases\n")

    def problems(frontmatter, body="# Widget\n", name="widget", extra=None):
        folder = os.path.join(skills, name)
        shutil.rmtree(folder, ignore_errors=True)
        write(os.path.join(folder, "SKILL.md"), ("---\n%s---\n\n" % frontmatter if frontmatter is not None else "")
              + body)
        for rel_path, text in (extra or {}).items():
            write(os.path.join(folder, rel_path), text)
        found = check_skills.check_skill(folder, root)
        shutil.rmtree(folder, ignore_errors=True)
        return found

    good = "name: widget\ndescription: %s\n" % GOOD_DESCRIPTION
    found = problems(good)
    check(found == [], "a skill with a parsing frontmatter, matching name and long description passes", found)

    # The classic failure: an unquoted description containing ": " isn't valid YAML, so the harness drops it.
    found = problems("name: widget\ndescription: Build a widget (a visual set only): a background preset. %s\n"
                     % GOOD_DESCRIPTION)
    check(any("doesn't parse" in p for p in found), "an unquoted description with ': ' fails to parse", found)
    found = problems('name: widget\ndescription: "Build a widget (a visual set only): a background preset. %s"\n'
                     % GOOD_DESCRIPTION)
    check(found == [], "the same description quoted passes", found)
    found = problems("name: widget\ndescription: Build a widget #1. %s\n" % GOOD_DESCRIPTION)
    check(any("doesn't parse" in p for p in found), "an unquoted description with ' #' fails to parse", found)

    found = problems(None)
    check(any("no frontmatter" in p for p in found), "a SKILL.md without frontmatter fails", found)
    folder = os.path.join(skills, "empty")
    os.makedirs(folder)
    found = check_skills.check_skill(folder, root)
    shutil.rmtree(folder)
    check(found == ["no SKILL.md"], "a skill folder without SKILL.md fails", found)

    found = problems("name: gadget\ndescription: %s\n" % GOOD_DESCRIPTION)
    check(any("doesn't match the folder" in p for p in found), "a name that isn't the folder name fails", found)
    found = problems("name: widget\n")
    check(any("description is missing" in p for p in found), "a missing description fails", found)
    found = problems("name: widget\ndescription: Builds widgets.\n")
    check(any("at least %d" % check_skills.MIN_DESCRIPTION in p for p in found), "a short description fails", found)
    exact = "/widget " + "x" * (check_skills.MIN_DESCRIPTION - 8)
    found = problems("name: widget\ndescription: %s\n" % exact)
    check(found == [], "a description of exactly the minimum length passes", found)

    found = problems(good, body="# Widget\n\nSee [units](../../../knowledge/units.md#ship-bases).\n")
    check(found == [], "a link that resolves (file and anchor) passes", found)
    found = problems(good, body="# Widget\n\nSee [units](../../../knowledge/gone.md).\n")
    check(any("broken link" in p and "gone.md" in p for p in found), "a link to a missing file fails", found)
    found = problems(good, body="# Widget\n\nSee [units](../../../knowledge/units.md#no-such-heading).\n")
    check(any("broken link" in p and "no-such-heading" in p for p in found), "a link to a missing anchor fails",
          found)
    found = problems(good, extra={"notes.md": "# Notes\n\n[bad](missing.md)\n"})
    check(any(p.startswith("notes.md:3: broken link") for p in found), "links in a skill's other .md files are checked",
          found)

    # A description is loaded by every session: capped at MAX_DESCRIPTION, and a skill's names its /command.
    exact = "Use /widget. " + "x" * (check_skills.MAX_DESCRIPTION - 13)
    found = problems("name: widget\ndescription: %s\n" % exact)
    check(found == [], "a description of exactly the maximum length passes", found)
    found = problems("name: widget\ndescription: %sx\n" % exact)
    check(any("keep it to %d" % check_skills.MAX_DESCRIPTION in p for p in found), "a description over the maximum fails",
          found)
    found = problems("name: widget\ndescription: %s\n" % GOOD_DESCRIPTION.replace("/widget", "widget"))
    check(any("doesn't name /widget" in p for p in found), "a skill description without its /name fails", found)

    # Named repo scripts must exist, and a tools/x.ps1 flag must be one of its parameters.
    write(os.path.join(root, "tools", "x.ps1"), "param(\n    [switch]$Fetch,\n    [string]$Branch\n)\n")
    write(os.path.join(root, "tools", "okf.py"), "")
    found = problems(good, body="# Widget\n\nRun `python3 tools/okf.py check`, then `./tools/x.ps1 -Fetch -Branch \"a b\"`.\n")
    check(found == [], "named scripts that exist, with real flags, pass", found)
    found = problems(good, body="# Widget\n\nRun `./tools/gone.ps1` and `tools/<script>.ps1`.\n")
    check(any("tools/gone.ps1, which doesn't exist" in p for p in found) and len(found) == 1,
          "a named script that doesn't exist fails; a <placeholder> is skipped", found)
    found = problems(good, body="# Widget\n\nRun `./tools/x.ps1 -Fetchh`.\n")
    check(any("-Fetchh isn't a parameter of tools/x.ps1" in p for p in found), "a flag the script lacks fails", found)
    found = problems(good, extra={"notes.md": "# Notes\n\n`tools/gone.py`\n"})
    check(any(p.startswith("notes.md:3: names tools/gone.py") for p in found), "a skill's other .md files are checked",
          found)

    # gh's default of 30 truncates a growing backlog silently.
    found = problems(good, body="# Widget\n\n`gh issue list --search x --state all --limit 50`\n")
    check(found == [], "gh issue list with --limit passes", found)
    found = problems(good, body="# Widget\n\n`gh issue list --label audit-x --state all`\n")
    check(any("has no --limit" in p for p in found), "gh issue list without --limit fails", found)

    budget = check_skills.okf.SKILL_BUDGET
    found = problems(good, body="# Widget\n" + "x" * (budget - 300) + "\n")
    check(found == [], "a skill under its size budget passes", found)
    found = problems(good, body="# Widget\n" + "x" * budget + "\n")
    check(any("over the 25 KB budget" in p for p in found), "a skill over its size budget fails", found)

    write(os.path.join(skills, "widget", "SKILL.md"), "---\n%s---\n# Widget\n" % good)
    out = io.StringIO()
    code = check_skills.main([], root, out=out)
    check(code == 0 and "0 problem(s) in 1 skill(s)" in out.getvalue(), "main passes a clean skills folder",
          out.getvalue())
    write(os.path.join(skills, "broken", "SKILL.md"), "---\nname: broken\ndescription: Short: bad\n---\n")
    out = io.StringIO()
    code = check_skills.main([], root, out=out)
    check(code == 1 and ".claude/skills/broken:" in out.getvalue(), "main exits 1 and names the broken skill",
          out.getvalue())
    out = io.StringIO()
    code = check_skills.main(["widget"], root, out=out)
    check(code == 0 and "1 skill(s)" in out.getvalue(), "main takes skill names", out.getvalue())

    # --- agents ---
    agents = os.path.join(root, ".claude", "agents")

    def agent_problems(frontmatter, name="runner", body="# Runner\n"):
        path = os.path.join(agents, name + ".md")
        write(path, "---\n%s---\n\n%s" % (frontmatter, body))
        found = check_skills.check_agent(path, root)
        os.remove(path)
        return found

    good_agent = "name: runner\ndescription: %s\nmodel: sonnet\n" % GOOD_DESCRIPTION
    found = agent_problems(good_agent)
    check(found == [], "an agent with a matching name, long description and a model passes", found)
    found = agent_problems("name: runner\ndescription: %s\n" % GOOD_DESCRIPTION)
    check(any("inherits the spawning session" in p for p in found), "an agent without a model fails", found)
    found = agent_problems("name: runner\ndescription: %s\nmodel: gpt\n" % GOOD_DESCRIPTION)
    check(any("model 'gpt'" in p for p in found), "an agent with an unknown model fails", found)
    found = agent_problems(good_agent, name="walker")
    check(any("doesn't match the file name" in p for p in found), "an agent named unlike its file fails", found)
    found = agent_problems(good_agent, body="Run `./tools/x.ps1 -Nope`.\n")
    check(any("-Nope isn't a parameter" in p for p in found), "an agent naming a missing flag fails", found)
    found = agent_problems(good_agent, body="[gone](../../knowledge/gone.md)\n")
    check(any("broken link" in p for p in found), "an agent with a broken link fails", found)

    shutil.rmtree(os.path.join(skills, "broken"))
    write(os.path.join(agents, "runner.md"), "---\n%s---\n" % good_agent)
    out = io.StringIO()
    code = check_skills.main([], root, out=out)
    check(code == 0 and "1 agent(s)" in out.getvalue(), "main checks the agents folder too", out.getvalue())
    write(os.path.join(agents, "runner.md"), "---\nname: runner\ndescription: %s\n---\n" % GOOD_DESCRIPTION)
    out = io.StringIO()
    code = check_skills.main([], root, out=out)
    check(code == 1 and ".claude/agents/runner.md:" in out.getvalue(), "main exits 1 and names the broken agent",
          out.getvalue())
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- the real skills ---
out = io.StringIO()
code = check_skills.main([], out=out)
check(code == 0, "every skill in .claude/skills and agent in .claude/agents passes", out.getvalue())

print("%d failure(s)" % failures)
sys.exit(1 if failures else 0)
