"""Tests the pure parts of tools/promote.py (no git, no gh).

Each behaviour has a pass case and a fail case. Run after changing promote.py:
    python tools/test-promote.py     (exit 0 = all passed)
"""

import importlib.util
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("promote", os.path.join(HERE, "promote.py"))
pm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pm)

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


def pr(number, title, head, merged):
    return {"number": number, "title": title, "headRefName": head, "headRefOid": "sha%d" % number, "mergedAt": merged}


# last_promotion: newest promotion wins; ordinary master PRs and unmerged ones don't count.
prs = [
    pr(749, "v0.3.6 - Arrows (autodev promotion)", "feature/promote-v0-3-6", "2026-10-06T20:44:32Z"),
    pr(773, "v0.3.8 - okf check", "feature/okf-vault", "2026-10-06T23:00:00Z"),
    pr(615, "v0.3.3 - Smoke (autodev promotion)", "feature/promote-v0-3-3", "2026-10-05T10:00:00Z"),
    pr(800, "v0.4.0 - Unmerged", "feature/promote-v0-4-0", None),
]
check(pm.last_promotion(prs)["number"] == 749, "last promotion is the newest merged promotion PR", pm.last_promotion(prs))
check(pm.last_promotion([prs[1]]) is None, "a master PR that isn't a promotion is never picked")
check(pm.last_promotion([pr(1, "v0.1.0 - Old", "promote-branch", "2026-01-01")]) is None,
      "a head branch without the prefix and no promotion title isn't a promotion")
check(pm.last_promotion([pr(2, "v0.1.0 - Old (autodev promotion)", "autodev", "2026-01-01")])["number"] == 2,
      "the title alone marks an older promotion opened from another branch")

# A non-promotion PR on a feature/promote-* branch (the /promote skill itself) isn't a promotion.
skill = pr(775, "v0.3.10 - /promote skill: promotion PRs list only the round", "feature/promote-skill", "2026-10-07T05:00:29Z")
check(pm.last_promotion(prs + [skill])["number"] == 749, "a feature/promote-<word> branch without a version isn't a promotion",
      pm.last_promotion(prs + [skill]))
check(pm.is_promotion(pr(798, "v0.3.11 - Round", "feature/promote-v0-3-11", "x")), "a feature/promote-v* branch is a promotion")

# A promotion whose head isn't in autodev's history is skipped for the newest one that is.
newer = pr(790, "v0.3.9 - Round (autodev promotion)", "feature/promote-v0-3-9", "2026-10-07T04:48:01Z")
check(pm.last_promotion(prs + [newer], lambda sha: sha != "sha790")["number"] == 749,
      "a promotion head outside autodev is skipped")
check(pm.last_promotion(prs + [newer], lambda sha: True)["number"] == 790, "a promotion head inside autodev is picked")

# pr_number: the trailing (#N), not an issue cited earlier.
check(pm.pr_number("Fix laggy command drag (#735) (#740)") == 740, "the squash PR is the last (#N)")
check(pm.pr_number("Minor ui changes") is None, "a direct commit has no PR number")
check(pm.pr_number("Mentions (#12) mid-subject then more") is None, "a (#N) not at the end isn't the PR")

# round_entries: oldest first; merge and master version commits dropped.
log = [  # git log order: newest first
    "v0.3.8 - okf check skips missing resources (#773)",
    "Minor ui changes",
    "Economy chart: legend toggles (#771)",
    "Merge remote-tracking branch 'origin/master' into HEAD",
    "Density tax curve (#762)",
]
entries = pm.round_entries(log)
check([e["subject"] for e in entries] == ["Density tax curve (#762)", "Economy chart: legend toggles (#771)", "Minor ui changes"],
      "round keeps this round's work, oldest first", entries)
check(all(e["pr"] != 773 for e in entries), "master's version-tagged commit from a sync is not this round's work")
check(pm.round_entries(["v0.3.7 - Split docs (#772)", "Merge branch 'master'", ""]) == [],
      "a round with only sync and merge commits is empty")

# closing_issues: all GitHub keywords, deduplicated, references without a keyword ignored.
body = "Fixes #731\nfixes: #732\nCloses #733 and resolved #734\nFixes #731\nSee #999, part of #998"
check(pm.closing_issues(body) == [731, 732, 733, 734], "closing keywords found once each", pm.closing_issues(body))
check(pm.closing_issues("Relates to #5, follow-up of #6, prefix #7") == [], "plain references don't close issues")
check(pm.closing_issues(None) == [], "an empty body closes nothing")
check(pm.closing_issues("Fixes #12a") == [], "a malformed reference doesn't match")

# next_version: next patch after master's latest.
check(pm.next_version("v0.3.8 - okf check (#773)") == "v0.3.9", "next patch after v0.3.8 is v0.3.9")
check(pm.next_version("v1.9.19 - x") == "v1.9.20", "patch rolls past two digits without touching minor")
try:
    pm.next_version("Initial empty commit")
    check(False, "a subject without a version raises")
except ValueError:
    check(True, "a subject without a version raises")
check(pm.next_version("Initial empty commit", first=True) == pm.FIRST_VERSION == "v0.1.0",
      "the first promotion (no version-tagged master commit) is v0.1.0")
check(pm.next_version("v0.1.0 - x", first=True) == "v0.1.1", "a version-tagged master still wins over first")

# render_body: included list, Fixes lines, no Fixes section when there are none.
entries = [{"subject": "Density tax curve (#762)", "pr": 762}, {"subject": "Minor ui changes", "pr": None}]
rendered = pm.render_body(entries, [750, 751], {"number": 749})
check("since the last promotion (#749)" in rendered, "body names the last promotion")
check("- #762 Density tax curve\n" in rendered, "a PR entry is listed by number without the trailing (#N)", rendered)
check("- Minor ui changes (direct commit)" in rendered, "a direct commit is listed as one")
check("Fixes #750\nFixes #751" in rendered, "each fixed issue gets its own Fixes line")
check(pm.closing_issues(rendered) == [750, 751], "the body closes exactly the round's issues")
check("Fixes" not in pm.render_body(entries, [], {"number": 749}), "no Fixes lines when no issue is fixed")
first = pm.render_body(entries, [], None)
check("the first promotion" in first and "#749" not in first, "the first promotion's body says so", first)

# included_prs: reads back the Included list render_body writes, and nothing else.
check(pm.included_prs(rendered) == [762], "included PRs read back from a rendered body", pm.included_prs(rendered))
check(pm.included_prs("- Minor ui changes (direct commit)\nSee #5\n -#6 x") == [], "direct commits and stray references aren't PRs")

print("\n%s" % ("all passed" if not failures else "%d FAILED" % failures))
sys.exit(1 if failures else 0)
