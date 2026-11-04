"""Tests tools/check-doc-links.py against a throwaway repo tree, then checks the real docs.

Each behaviour has a pass case (a link that resolves) and a fail case (one that must be reported). Run after changing
check-doc-links.py:
    python tools/test-check-doc-links.py     (exit 0 = all passed)
"""

import contextlib
import importlib.util
import io
import os
import shutil
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("check_doc_links", os.path.join(HERE, "check-doc-links.py"))
cdl = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cdl)

failures = 0


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
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)


# --- slugs: GitHub's rules ---
check(cdl.slugify("Every key is a row") == "every-key-is-a-row", "a heading slug is lowercase with hyphens for spaces")
check(cdl.slugify("Save system (Game Manager/Save Manager)") == "save-system-game-managersave-manager",
      "punctuation is dropped, not turned into hyphens")
check(cdl.slugify("Leaving without dying: `UnitMode.Fled`") == "leaving-without-dying-unitmodefled",
      "inline code in a heading keeps its text")
check(cdl.slugify("Direct orders: a heavy weight, not an override") != "direct-orders:-a-heavy-weight,-not-an-override",
      "a slug never keeps colons or commas")
check(cdl.slugify("snake_case and-hyphen") == "snake_case-and-hyphen", "underscores and hyphens survive")

found = cdl.anchors("# The ghost\n\n## Other\n\n### The ghost\n\n```\n# not a heading\n```\n<a id=\"custom-id\"></a>\n")
check("the-ghost" in found and "the-ghost-1" in found, "a repeated heading gets -1", found)
check("the-ghost-2" not in found, "a heading seen twice has no -2", found)
check("custom-id" in found, "an explicit <a id> is an anchor", found)
check("not-a-heading" not in found, "a # line inside a code fence is not a heading", found)

# --- link parsing ---
check(cdl.links("see [a](x.md) and ![b](img/y.png)") == ["x.md", "img/y.png"], "links and images are both found")
check(cdl.links("[a](Some%20(Thing).md)") == ["Some%20(Thing).md"], "balanced parentheses stay in the target")
check(cdl.links("[a](<Folder Name/x.md>)") == ["Folder Name/x.md"], "an <angle-bracket> target keeps its spaces")
check(cdl.links('[a](x.md "Title")') == ["x.md"], "a link title is not part of the target")
check(cdl.links("plain [text] (not a link)") == [], "a bracket followed by a space is not a link")

# --- whole files against a throwaway repo ---
root = tempfile.mkdtemp(prefix="check-doc-links-test-")
try:
    write(os.path.join(root, "docs", "4. Tax Rules", "Density.md"), "# Density\n\n## Spending it\n")
    write(os.path.join(root, "knowledge", "impl", "target.md"),
          "# Target\n\n## The ghost\n\n## The ghost\n\n<a id=\"every-key-is-a-row\"></a>**Every key is a row.**\n")
    write(os.path.join(root, "knowledge", "good.md"), "\n".join([
        "# Good",
        "## Local heading",
        "[same file](#local-heading)",
        "[sibling](impl/target.md)",
        "[second ghost](impl/target.md#the-ghost-1)",
        "[explicit id](impl/target.md#every-key-is-a-row)",
        "[encoded](../docs/4.%20Tax%20Rules/Density.md#spending-it)",
        "[repo root](/docs/4.%20Tax%20Rules/Density.md)",
        "[folder](impl/)",
        "[external](https://example.com/missing.md) [mail](mailto:x@example.com)",
        "`[in code](missing.md)`",
        "```",
        "[in a fence](missing.md)",
        "```",
    ]) + "\n")
    write(os.path.join(root, "knowledge", "bad.md"), "\n".join([
        "# Bad",
        "[old folder](../docs/2.%20Tax%20Rules/Density.md)",
        "[no heading](impl/target.md#the-ghost-2)",
        "[no local heading](#nowhere)",
        "[renamed heading](../docs/4.%20Tax%20Rules/Density.md#earning-it)",
    ]) + "\n")

    good = cdl.check_file(os.path.join(root, "knowledge", "good.md"), root)
    check(good == [], "every resolvable link passes (encoded paths, anchors, ids, repo-root, folders; code ignored)", good)

    bad = cdl.check_file(os.path.join(root, "knowledge", "bad.md"), root)
    reported = {(line, reason) for line, _, reason in bad}
    check((2, "no such file") in reported, "a link into a renumbered folder is reported", bad)
    check((3, "no heading #the-ghost-2") in reported, "an anchor past the repeats is reported", bad)
    check((4, "no heading #nowhere") in reported, "a missing same-file anchor is reported", bad)
    check((5, "no heading #earning-it") in reported, "a missing anchor in another folder's doc is reported", bad)
    check(len(bad) == 4, "nothing else is reported", bad)

    with contextlib.redirect_stdout(io.StringIO()) as out:
        code_bad = cdl.main(["knowledge"], root)
    check(code_bad == 1 and "knowledge/bad.md:2:" in out.getvalue(), "main exits 1 and names file:line when links break",
          out.getvalue())
    os.remove(os.path.join(root, "knowledge", "bad.md"))
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code_good = cdl.main(["knowledge"], root)
    check(code_good == 0, "main exits 0 when every link resolves", out.getvalue())
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- the default roots include CLAUDE.md and .claude/agents ---
root = tempfile.mkdtemp(prefix="check-doc-links-roots-")
try:
    write(os.path.join(root, "knowledge", "real.md"), "# Real\n\n## There\n")
    write(os.path.join(root, "CLAUDE.md"), "# Project\n\n[ok](knowledge/real.md#there) [gone](knowledge/gone.md)\n")
    write(os.path.join(root, ".claude", "agents", "helper.md"),
          "# Helper\n\n[ok](../../knowledge/real.md) [stale](../../knowledge/real.md#elsewhere)\n")
    check({"CLAUDE.md", ".claude/agents"} <= set(cdl.DEFAULT_ROOTS), "the default roots name CLAUDE.md and .claude/agents",
          cdl.DEFAULT_ROOTS)
    with contextlib.redirect_stdout(io.StringIO()) as out:
        cdl.main([], root)
    report = out.getvalue()
    check("CLAUDE.md:3: knowledge/gone.md (no such file)" in report and "CLAUDE.md:3: knowledge/real.md#there" not in report,
          "a no-argument run checks CLAUDE.md's links (pass: a good link; fail: a missing file)", report)
    check(".claude/agents/helper.md:3: ../../knowledge/real.md#elsewhere (no heading #elsewhere)" in report
          and "../../knowledge/real.md (" not in report,
          "a no-argument run checks .claude/agents (pass: a good link; fail: a missing anchor)", report)
    write(os.path.join(root, "CLAUDE.md"), "# Project\n\n[ok](knowledge/real.md#there)\n")
    write(os.path.join(root, ".claude", "agents", "helper.md"), "# Helper\n\n[ok](../../knowledge/real.md#there)\n")
    with contextlib.redirect_stdout(io.StringIO()) as out:
        cdl.main([], root)
    check("0 broken link(s) in 3 file(s) checked" in out.getvalue(), "fixed links pass, the three roots all walked",
          out.getvalue())
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- guard: every tools/ file is named in repo-structure.md, a skill or a hook ---
root = tempfile.mkdtemp(prefix="check-doc-links-tools-")
try:
    for name in ["tools/okf.py", "tools/hooked.sh", "tools/skilled.ps1", "tools/perf/census-saves.ps1",
                 "tools/builders/build-ui.ps1", "tools/test-okf.py", "tools/fixtures/sample.json",
                 "tools/data/tax-curve.json"]:
        write(os.path.join(root, name), "x\n")
    write(os.path.join(root, "knowledge", "repo-structure.md"), "\n".join([
        "# Tools",
        "| `okf.py` | `perf/census-*.ps1` | one `build-<part>.ps1` per part | `data/tax-curve.json` |",
        "A too-loose glob names nothing: `*.ps1`, `<name>.json`.",
    ]) + "\n")
    write(os.path.join(root, ".claude", "skills", "x", "SKILL.md"), "Run `tools/skilled.ps1`.\n")
    write(os.path.join(root, ".githooks", "pre-commit"), "sh tools/hooked.sh\n")
    check(cdl.orphan_tools(root) == [],
          "a tool named by path, file name, glob or <template> in repo-structure.md, a skill or a hook is not an "
          "orphan; tests and fixtures need no row", cdl.orphan_tools(root))

    write(os.path.join(root, "tools", "orphan.py"), "x\n")
    write(os.path.join(root, "tools", "data", "spec.json"), "x\n")
    write(os.path.join(root, "tools", "skilled.ps1.bak"), "x\n")
    orphans = cdl.orphan_tools(root)
    check(orphans == ["tools/orphan.py", "tools/skilled.ps1.bak", "tools/data/spec.json"],
          "a tool nothing names is reported, even when a name it extends is documented or a too-loose glob would match it",
          orphans)
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code_orphan = cdl.main([], root)
    check(code_orphan == 1 and "tools/orphan.py: not named" in out.getvalue(),
          "main with no arguments runs the guard and exits 1 on an orphan", out.getvalue())
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code_files = cdl.main(["knowledge"], root)
    check(code_files == 0, "main given paths checks only their links, not the repo guards", out.getvalue())
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- guard: every folder repo-structure.md maps exists ---
root = tempfile.mkdtemp(prefix="check-doc-links-folders-")
try:
    for folder in ["tools", "Mod/Systems", "UI/src/charts", "Tests"]:
        os.makedirs(os.path.join(root, folder))
    write(os.path.join(root, "knowledge", "repo-structure.md"), "\n".join([
        "# Repo Structure",
        "## Top level",
        "| Folder | Purpose |",
        "|---|---|",
        "| `tools/` | Scripts (`tools/missing-sub/` in parentheses isn't checked) |",
        "| `bin/`, `obj/` | Build output / gitignored |",
        "| `Tests/` | xunit |",
        "| `Later/` | Not yet created |",
        "## Mod/",
        "| `Systems/` | ECS systems |",
        "| `Old Folder/` | A folder that was deleted |",
        "## `UI/src/` (TypeScript)",
        "| `<feature>/` | Templates aren't checked |",
        "| `charts/`, `gone/` (`sub/`) | Components |",
        "## Assemblies",
        "| `FiscalOverhaul` | Not a folder |",
    ]) + "\n")
    missing = cdl.missing_mapped_folders(root)
    check([folder for _, folder in missing] == ["Mod/Old Folder/", "UI/src/gone/"],
          "a mapped folder that doesn't exist is reported under its section's prefix; present, gitignored, not-yet, "
          "templated and parenthesised ones aren't", missing)
    os.makedirs(os.path.join(root, "Mod", "Old Folder"))
    os.makedirs(os.path.join(root, "UI", "src", "gone"))
    check(cdl.missing_mapped_folders(root) == [], "once the folders exist nothing is reported",
          cdl.missing_mapped_folders(root))
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- guard: every top-level tools/ script has a test twin ---
root = tempfile.mkdtemp(prefix="check-doc-links-twins-")
tested_by, no_twin = cdl.TESTED_BY, cdl.NO_TEST_TWIN
try:
    for name in ["tools/okf.py", "tools/test-okf.py", "tools/hooked.sh", "tools/test-hooked.ps1", "tools/shared-common.ps1",
                 "tools/covered.py", "tools/test-group.py", "tools/legacy.sh", "tools/sub/deep.py", "tools/data.json"]:
        write(os.path.join(root, name), "covers covered.py\n")
    cdl.TESTED_BY = {"covered.py": "test-group.py"}
    cdl.NO_TEST_TWIN = {"legacy.sh": "reason"}
    check(cdl.twin_problems(root) == [],
          "a script with a twin (any extension), a -common helper, a TESTED_BY script its test names, a listed one, "
          "and files outside the top level or not scripts all pass", cdl.twin_problems(root))
    write(os.path.join(root, "tools", "untested.ps1"), "x\n")
    problems = cdl.twin_problems(root)
    check(problems == ["tools/untested.ps1 has no test: add tools/test-untested.<py|ps1|sh> with a pass and a fail case "
                       "(or name the test that covers it in TESTED_BY in tools/check-doc-links.py)"],
          "fail case: a new script without a test fails", problems)
    os.remove(os.path.join(root, "tools", "untested.ps1"))
    write(os.path.join(root, "tools", "test-group.py"), "names nothing\n")
    problems = cdl.twin_problems(root)
    check(len(problems) == 1 and "never names it" in problems[0], "fail case: a TESTED_BY test that doesn't name its script",
          problems)
    write(os.path.join(root, "tools", "test-group.py"), "covers covered.py\n")
    write(os.path.join(root, "tools", "test-legacy.sh"), "x\n")
    problems = cdl.twin_problems(root)
    check(len(problems) == 1 and "tools/legacy.sh has a test now" in problems[0],
          "the ratchet: a NO_TEST_TWIN script that gains a test must leave the list", problems)
    os.remove(os.path.join(root, "tools", "test-legacy.sh"))
    os.remove(os.path.join(root, "tools", "legacy.sh"))
    problems = cdl.twin_problems(root)
    check(len(problems) == 1 and "doesn't exist: delete the row" in problems[0], "a NO_TEST_TWIN row for a deleted script is stale",
          problems)
    fixture = os.path.join(root, "fixture")
    write(os.path.join(fixture, "tools", "okf.py"), "x\n")
    write(os.path.join(fixture, "tools", "check-skills.py"), "x\n")
    problems = cdl.twin_problems(fixture)
    check(problems == [], "a fixture whose tools/ holds no test at all (test-git-hooks.sh's doc repo) is not judged, "
                          "so its copied tools and the real list's rows don't read missing or stale", problems)
    write(os.path.join(fixture, "tools", "test-okf.py"), "x\n")
    problems = cdl.twin_problems(fixture)
    check(len(problems) == 2 and any("tools/check-skills.py has no test" in p for p in problems)
          and any("doesn't exist: delete the row" in p for p in problems),
          "fail case: with one tool test there, it is a tools folder again and both rules apply", problems)
    cdl.TESTED_BY, cdl.NO_TEST_TWIN = tested_by, no_twin
    check(len(cdl.NO_TEST_TWIN) <= 1, "NO_TEST_TWIN only shrinks (one untested wrapper: build.ps1)",
          len(cdl.NO_TEST_TWIN))
finally:
    cdl.TESTED_BY, cdl.NO_TEST_TWIN = tested_by, no_twin
    shutil.rmtree(root, ignore_errors=True)

# --- the real docs ---
with contextlib.redirect_stdout(io.StringIO()) as out:
    code = cdl.main([])
check(code == 0, "knowledge/, .claude/skills, .claude/agents and CLAUDE.md have no broken links, every tools/ file is documented and every mapped "
      "folder exists", out.getvalue())

print("%d failure(s)" % failures)
sys.exit(1 if failures else 0)
