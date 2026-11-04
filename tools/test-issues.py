"""Tests tools/issues.py against sample issues and a stand-in for gh, in a throwaway folder.

Each behaviour has a pass case and a fail case. Run after changing issues.py:
    python tools/test-issues.py     (exit 0 = all passed)
"""

import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("issues", os.path.join(HERE, "issues.py"))
iss = importlib.util.module_from_spec(spec)
spec.loader.exec_module(iss)

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


def issue(number, title, *labels, body="Body of %d.", created="2026-10-05T00:00:00Z"):
    return {"number": number, "title": title, "labels": [{"name": l} for l in labels],
            "body": body % number if "%d" in body else body, "url": "https://example/%d" % number,
            "createdAt": created}


SAMPLE = [
    issue(10, "Old perf cleanup", "area:taxation", "tech-debt"),
    issue(11, "Perf bug", "area:taxation", "bug"),
    issue(12, "Urgent perf enhancement", "area:taxation", "enhancement", "priority:high"),
    issue(13, "Perf decision", "area:taxation", "bug", "needs-decision"),
    issue(14, "Low perf nicety", "area:taxation", "bug", "priority:low"),
    issue(20, "Smoke too dark", "area:chart", "enhancement"),
    issue(21, "Planned smoke rework", "area:chart", "enhancement", "tracking"),
    issue(30, "Nobody triaged me", "bug"),
    issue(31, "Two areas", "area:chart", "area:ui"),
    issue(32, "Made-up area", "area:sound"),
    issue(40, "Docs only", "area:mod", "documentation"),
    issue(41, "Two types", "area:mod", "bug", "tech-debt"),
    issue(42, "Old decision", "area:mod", "enhancement", "needs-decision", created="2026-10-01T00:00:00Z"),
]


class FakeGh:
    """Stands in for subprocess.run: answers `issue list` with SAMPLE and records every call."""

    def __init__(self, fail=False):
        self.calls = []
        self.fail = fail

    def __call__(self, cmd, **kwargs):
        self.calls.append(cmd)
        self.cwds = getattr(self, "cwds", []) + [kwargs.get("cwd")]

        class R:
            pass

        r = R()
        r.returncode = 1 if self.fail else 0
        r.stderr = "boom" if self.fail else ""
        r.stdout = json.dumps(SAMPLE) if cmd[1:3] == ["issue", "list"] else ""
        return r


def run_main(argv, gh=None):
    out = io.StringIO()
    code = iss.main(argv, run=gh or FakeGh(), out=out)
    return code, out.getvalue()


issues = [dict(i, labels=[l["name"] for l in i["labels"]]) for i in SAMPLE]

# Grouping: exactly one known area files under it; none, several or unknown go to untriaged.
groups = iss.group_by_area(issues)
check([i["number"] for i in groups["chart"]] == [20, 21], "an issue with one known area is grouped under it",
      [i["number"] for i in groups["chart"]])
check(sorted(i["number"] for i in groups["untriaged"]) == [30, 31, 32],
      "no area, two areas and an unknown area are untriaged", [i["number"] for i in groups["untriaged"]])
check(all(i["number"] != 31 for i in groups["ui"]), "an issue with two areas isn't counted under either")

# Order: priority:high first, then bug, enhancement, tech-debt, oldest first, priority:low last.
order = [i["number"] for i in groups["taxation"]]
check(order == [12, 11, 13, 10, 14], "batch order is priority, then type, then age", order)
check(order != sorted(order), "order isn't plain issue number")

# Triage lists exactly the issues without one known area, with the reason.
reasons = {i["number"]: r for i, r in iss.untriaged(issues)}
check(set(reasons) == {30, 31, 32, 40, 41}, "triage lists issues missing an area or a type", reasons)
check(10 not in reasons and 20 not in reasons, "triage skips correctly labelled issues")
check(reasons.get(31, "").startswith("several areas") and reasons.get(32, "").startswith("unknown area"),
      "triage says why", reasons)
check("no type" in reasons.get(40, "") and "no type" in reasons.get(31, ""),
      "an issue with no bug, enhancement or tech-debt label needs a type (documentation isn't one)", reasons)
check(reasons.get(41, "").startswith("several types: bug, tech-debt"), "two types are flagged", reasons)
check(30 in reasons and "type" not in reasons[30], "an issue with one type isn't flagged for its type", reasons)
check(42 not in reasons, "one area and one type pass triage")
code, text = run_main(["triage"])
check(code == 1 and "5 of 13" in text and "#40 Docs only" in text, "triage exits 1 while issues need an area or type", text)
clean = FakeGh()
saved = list(SAMPLE)
SAMPLE[:] = [s for s in SAMPLE if s["number"] < 30]
code, text = run_main(["triage"], clean)
SAMPLE[:] = saved
check(code == 0, "triage exits 0 when every issue has one area", text)

# Type rank: bug, enhancement, tech-debt; documentation no longer ranks as a type.
by_number = {i["number"]: i for i in issues}
check(iss.sort_key(by_number[10])[1] == 2 and iss.sort_key(by_number[40])[1] == len(iss.TYPES),
      "tech-debt ranks as a type and documentation sorts after every type",
      (iss.sort_key(by_number[10]), iss.sort_key(by_number[40])))

# Decisions: every needs-decision issue, oldest first, with its age; nothing else.
queue = [i["number"] for i in iss.decisions(issues)]
check(queue == [42, 13], "decisions lists needs-decision issues oldest first", queue)
check(11 not in queue, "an issue without needs-decision isn't in the queue")
import datetime
text = iss.decisions_text(issues, datetime.date(2026, 10, 10))
check("2 open issues wait on a decision" in text and "#42 Old decision (9 days, mod)" in text,
      "decisions says how many wait and how old each is", text)

# Fixed: open issues a merged PR closes; on master unless an autodev PR isn't promoted yet.
PRS = [
    {"number": 100, "baseRefName": "master", "body": "Fixes #11"},
    {"number": 101, "baseRefName": "autodev", "body": "Some work.\n\nFixes #20\nFixes #999"},
    {"number": 102, "baseRefName": "autodev", "body": "closes: #14"},
    {"number": 103, "baseRefName": "autodev", "body": "Part of #10"},
]
found = [(i["number"], pr["number"], reached)
         for i, pr, reached in iss.fixed_open(issues, PRS, lambda pr: pr["number"] == 101)]
check(found == [(11, 100, True), (20, 101, True), (14, 102, False)],
      "fixed finds each open issue a merged PR closes, and whether it reached master", found)
check(all(n != 10 for n, _, _ in found), "a Part of reference isn't a fix")
saved_prs, saved_check = iss.merged_prs, iss.promoted_check
try:
    iss.merged_prs = lambda run: PRS
    iss.promoted_check = lambda run: (lambda pr: False)
    code, text = run_main(["fixed"])
    check(code == 1 and "#11 Perf bug: fixed by #100 (on master: close it)" in text,
          "fixed exits 1 when a fix on master left its issue open", text)
    iss.merged_prs = lambda run: PRS[1:]
    code, text = run_main(["fixed"])
    check(code == 0 and "closes at the next promotion" in text, "fixed exits 0 when every fix awaits promotion", text)
finally:
    iss.merged_prs, iss.promoted_check = saved_prs, saved_check

# Batch planning: needs-decision is held back unless asked for; --limit caps the chosen list.
chosen, held = iss.plan_batch(issues, "taxation")
check(13 not in [i["number"] for i in chosen] and [i["number"] for i in held] == [13],
      "needs-decision issues are held back", ([i["number"] for i in chosen], [i["number"] for i in held]))
chosen, held = iss.plan_batch(issues, "taxation", include_decisions=True)
check(13 in [i["number"] for i in chosen] and not held, "--include-decisions briefs them too")
chosen, _ = iss.plan_batch(issues, "taxation", limit=2)
check([i["number"] for i in chosen] == [12, 11], "--limit takes the first N in batch order",
      [i["number"] for i in chosen])

# Readiness: labels first, then the template's fields (knowledge/issues.md#readiness).
FULL = "**Seen:** x\n**Where:** `Mod/Systems/DensityTaxSystems.cs:53`\n**Test:** pass / fail\n**Done when:** y"


def ready(*labels, body=FULL):
    return iss.readiness({"labels": list(labels), "body": body})


check(ready("bug") == "spelled-out", "a site, a Test and a Done when make an issue spelled out", ready("bug"))
check(ready("bug", body=FULL.replace("**Done when:** y", "")) == "open", "a missing Done when leaves it open")
check(ready("bug", body=FULL.replace("**Test:**", "Test:")) == "open",
      "a Test only in prose isn't the template's field", ready("bug", body=FULL.replace("**Test:**", "Test:")))
check(ready("bug", body=FULL.replace("DensityTaxSystems.cs:53", "DensityTaxSystems.cs")) == "open",
      "a path without a line isn't a site")
check(ready("enhancement", body="**Change:** a minimap\n**Test:** t\n**Done when:** d") == "plan",
      "an enhancement with no site needs a plan")
check(ready("enhancement") == "spelled-out", "an enhancement already spelled out doesn't need a plan", ready("enhancement"))
check(ready("bug", "needs-plan") == "plan", "needs-plan asks for a plan whatever the body says")
check(ready("bug", "needs-plan", "needs-decision") == "decision", "an open decision outranks everything")
check(ready("enhancement", "tracking") == "tracking", "a tracking parent is tracking")
check(iss.has_field("**Done when**: x", "Done when") and iss.has_field("**done when:** x", "Done when"),
      "the field matches with the colon inside or outside the bold, in any case")

# A tracking parent is held back even with --include-decisions.
chosen, held = iss.plan_batch(issues, "chart", include_decisions=True)
check([i["number"] for i in chosen] == [20] and [i["number"] for i in held] == [21],
      "tracking parents are held back even with --include-decisions",
      ([i["number"] for i in chosen], [i["number"] for i in held]))

# Batch writing: README plus one brief per issue, carrying the issue body and a Fixes line.
tmp = tempfile.mkdtemp()
try:
    out_dir = os.path.join(tmp, "batch")
    code, text = run_main(["batch", "taxation", "--out", out_dir])
    files = sorted(os.listdir(out_dir)) if os.path.isdir(out_dir) else []
    check(code == 0 and "README.md" in files and len(files) == 5, "batch writes a README and four briefs", (code, files))
    check(files and files[0] == "01-12-urgent-perf-enhancement.md", "briefs are numbered in batch order", files)
    brief = open(os.path.join(out_dir, files[0]), encoding="utf-8").read() if files else ""
    check("Body of 12." in brief and "Fixes #12" in brief, "a brief carries the issue body and closes it", brief)
    check("Body of 11." not in brief, "a brief carries only its own issue")
    check("Part of #12" in brief and "Left undone" in brief and "Part of #11" not in brief,
          "a brief says how to reference its own issue when a slice is left undone", brief)
    readme = open(os.path.join(out_dir, "README.md"), encoding="utf-8").read() if "README.md" in files else ""
    check("#13 Perf decision" in readme and "Held back" in readme, "README lists the held-back decisions", readme)
    check("| 1 | [Urgent perf enhancement](01-12-urgent-perf-enhancement.md) | #12 | plan |" in readme,
          "README links each brief with its readiness", readme)
    check("Readiness: plan (/plan-feature first" in brief, "a brief says its readiness and what it suggests", brief)

    vfx_dir = os.path.join(tmp, "chart")
    run_main(["batch", "chart", "--out", vfx_dir])
    vfx_readme = open(os.path.join(vfx_dir, "README.md"), encoding="utf-8").read()
    check("tracking issues" in vfx_readme and "#21 Planned smoke rework" in vfx_readme
          and "Held back: needs a decision" not in vfx_readme,
          "README lists tracking parents under their own heading", vfx_readme)

    empty_dir = os.path.join(tmp, "empty")
    code, text = run_main(["batch", "docs", "--out", empty_dir])
    check(code == 1 and not os.path.exists(empty_dir), "an empty area writes nothing and exits 1", text)
finally:
    shutil.rmtree(tmp)

code, text = run_main(["batch", "sound"])
check(code == 2 and "Unknown area" in text, "an unknown area is refused", text)
code, text = run_main(["board", "--area", "chart"])
check("#20 Smoke too dark" in text and "#11" not in text, "board --area shows only that area", text)
check("#20 Smoke too dark [enhancement] {plan}" in text and "{tracking}" in text, "board shows each issue's readiness", text)
code, text = run_main(["board"])
check("## untriaged (3)" in text and "## taxation (5)" in text, "board shows every area and the untriaged", text)
check("## docs" not in text, "board skips empty areas")

# Labels: dry run calls nothing; --apply creates every area and priority label.
gh = FakeGh()
run_main(["labels"], gh)
check(gh.calls == [], "labels without --apply runs no gh command", gh.calls)
gh = FakeGh()
run_main(["labels", "--apply"], gh)
made = [c[3] for c in gh.calls]
check(len(made) == len(iss.AREAS) + len(iss.PRIORITIES) + len(iss.STATUS_LABELS) and "area:chart" in made
      and "priority:high" in made and "needs-plan" in made and "tracking" in made,
      "labels --apply creates every label", made)
check(gh.cwds and all(c == iss.REPO for c in gh.cwds) and not any("--repo" in c for c in gh.calls),
      "gh always runs in the checkout, so it finds the repo from the git remote (no hard-coded owner)", gh.calls)

try:
    iss.gh(["issue", "list"], run=FakeGh(fail=True))
    check(False, "a failing gh call raises")
except RuntimeError as e:
    check("boom" in str(e), "a failing gh call raises with its stderr", e)

print("\n%s" % ("All passed." if failures == 0 else "%d failed." % failures))
sys.exit(1 if failures else 0)
