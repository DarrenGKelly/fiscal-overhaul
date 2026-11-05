"""Tests the pure parts of tools/promote.py (no git, no gh).

Each behaviour has a pass case and a fail case. Run after changing promote.py:
    python tools/test-promote.py     (exit 0 = all passed)
"""

import importlib.util
import os
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET

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

# round_head: a promotion cut from autodev plus a stamp commit records where the round ended.
stamped = dict(pr(810, "v1.0.0 - Release (autodev promotion)", "feature/promote-v1-0-0", "2026-11-01T10:00:00Z"),
               body="Promotes ...\n\nRound head: `abc1234def` (the `autodev` commit ...).\n")
check(pm.round_head(stamped) == "abc1234def", "the body's Round head line names the round's end", pm.round_head(stamped))
check(pm.round_head(prs[0]) == "sha749", "without the line the round ends at the PR's head")
check(pm.last_promotion(prs + [stamped], lambda sha: sha != "sha810")["number"] == 810,
      "a promotion whose head (the stamp commit) isn't in autodev is still found by its round head")
check(pm.last_promotion(prs + [stamped], lambda sha: sha != "abc1234def")["number"] == 749,
      "a promotion whose round head isn't in autodev is skipped")
headed = pm.render_body(entries, [], {"number": 749}, head="0123456789abcdef")
check(pm.round_head(dict(prs[0], body=headed)) == "0123456789abcdef", "render_body's Round head line reads back", headed)
check("Round head" not in rendered, "no Round head line without a head")

# next_version with the version autodev declares.
check(pm.version_key("v1.2.3") == (1, 2, 3) and pm.version_key("1.2.3") == (1, 2, 3), "versions parse with or without the v")
check(pm.version_key("1.0") is None and pm.version_key(None) is None, "a two-part or missing version doesn't parse")
check(pm.next_version("Initial", first=True, declared="1.0.0") == "v1.0.0",
      "the first promotion takes autodev's declared 1.0.0 over v0.1.0")
check(pm.next_version("v1.0.0 - Release (#9)", declared="1.0.0") == "v1.0.1",
      "a declared version already released loses to the next patch")
check(pm.next_version("v1.0.3 - x", declared="1.1.0") == "v1.1.0", "a declared minor bump wins")
check(pm.next_version("v1.0.3 - x", declared="1.0") == "v1.0.4", "an unparseable declared version is ignored")

# stamp_config: ModVersion and one ChangeLog, the template's comments kept.
CONFIG = """<Publish>
	<!--Version of the mod-->
	<!--<ModVersion Value="9.9.9" />-->
	<ModVersion Value="1.0" />
	<GameVersion Value="1.6.*" />
	<!--Change log for new version. Single line or multi line.-->
	<ChangeLog Value="" />
	<!--<ChangeLog>
	</ChangeLog>-->
	<AccessLevel Value="Public" />
</Publish>
"""
check(pm.declared_version(CONFIG) == "1.0", "declared_version skips a commented-out ModVersion", pm.declared_version(CONFIG))
check(pm.declared_version("<Publish />") is None, "no ModVersion declares nothing")
changelog = "- Budget chart: legend toggles\n- Taxes & fees <per density>"
out = pm.stamp_config(CONFIG, "1.2.3", changelog)
tree = ET.fromstring(out)
check(tree.find("ModVersion").get("Value") == "1.2.3", "stamp_config sets ModVersion", out)
check(len(tree.findall("ChangeLog")) == 1 and tree.find("ChangeLog").text.strip() == changelog,
      "... leaving one ChangeLog element whose text is the changelog (escaped, so it parses)", out)
check("<!--<ChangeLog>\n\t</ChangeLog>-->" in out and '<!--<ModVersion Value="9.9.9" />-->' in out,
      "... keeping the commented-out examples untouched", out)
check(out.count("\n") == CONFIG.count("\n") + changelog.count("\n") + 2,
      "... and every other line (the ChangeLog block adds its own)", out)
again = pm.stamp_config(out, "1.2.4", "- Other")
check(ET.fromstring(again).find("ChangeLog").text.strip() == "- Other" and len(ET.fromstring(again).findall("ChangeLog")) == 1,
      "re-stamping replaces the element ChangeLog", again)
two = CONFIG.replace("<AccessLevel", "<ChangeLog>stale</ChangeLog>\n\t<AccessLevel")
check(len(ET.fromstring(pm.stamp_config(two, "1.2.3", changelog)).findall("ChangeLog")) == 1,
      "a second live ChangeLog (ModPublisher would read the last) is removed")
none = CONFIG.replace('\t<ChangeLog Value="" />\n', "")
check(ET.fromstring(pm.stamp_config(none, "1.2.3", changelog)).find("ChangeLog").text.strip() == changelog,
      "a config without a ChangeLog gets one")
crlf = pm.stamp_config(CONFIG.replace("\n", "\r\n"), "1.2.3", changelog)
check(crlf.count("\n") == crlf.count("\r\n") == out.count("\n"), "a CRLF config keeps CRLF line endings throughout", repr(crlf))
check(pm.stamp_config(CONFIG, "1.2.3", changelog).count("\r") == 0, "an LF config gets no CR")
try:
    pm.stamp_config('<Publish>\n\t<!--<ModVersion Value="1" />-->\n</Publish>', "1.2.3", changelog)
    check(False, "a config without a live ModVersion raises")
except ValueError:
    check(True, "a config without a live ModVersion raises")

# stamp_mod_json: the version only.
mod_json = '{\n  "id": "FiscalOverhaul",\n  "version": "0.1.0",\n  "dependencies": []\n}\n'
check(pm.stamp_mod_json(mod_json, "1.2.3") == mod_json.replace("0.1.0", "1.2.3"), "stamp_mod_json sets the version, nothing else")
try:
    pm.stamp_mod_json('{ "id": "x" }', "1.2.3")
    check(False, "a mod.json without a version raises")
except ValueError:
    check(True, "a mod.json without a version raises")

# changelog_from_body: the Included entries, nothing else.
check(pm.changelog_from_body(rendered) == "- Density tax curve\n- Minor ui changes",
      "the ChangeLog draft lists the Included entries", pm.changelog_from_body(rendered))
check(pm.changelog_from_body("## Issues closed\n- not this\nFixes #1") == "", "a body without Included entries drafts nothing")

# stamp: writes a checkout's files, keeping the config's BOM; refuses going backwards or an empty round.
tmp = tempfile.mkdtemp(prefix="fo-test-promote-")
saved_repo = pm.REPO
try:
    pm.REPO = tmp
    os.makedirs(os.path.join(tmp, "Mod", "Properties"))
    os.makedirs(os.path.join(tmp, "UI"))
    config_path = os.path.join(tmp, pm.CONFIG)
    with open(config_path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(CONFIG)
    with open(os.path.join(tmp, pm.MOD_JSON), "w", encoding="utf-8", newline="") as f:
        f.write(mod_json)
    body_path = os.path.join(tmp, "body.md")
    with open(body_path, "w", encoding="utf-8") as f:
        f.write(rendered)
    check(pm.stamp("v1.0.0", body_path) == 0, "stamp v1.0.0 succeeds")
    with open(config_path, "rb") as f:
        raw = f.read()
    check(raw.startswith(b"\xef\xbb\xbf") and pm.declared_version(raw.decode("utf-8-sig")) == "1.0.0",
          "... writing ModVersion 1.0.0 and keeping the BOM")
    with open(os.path.join(tmp, pm.MOD_JSON), encoding="utf-8") as f:
        check('"version": "1.0.0"' in f.read(), "... and UI/mod.json's version")
    for args, why in ((("v0.9.0", body_path), "fail case: a version below the declared one is refused"),
                      (("1.0", body_path), "fail case: a version that isn't vX.Y.Z is refused")):
        try:
            pm.stamp(*args)
            check(False, why)
        except SystemExit:
            check(True, why)
    with open(body_path, "w", encoding="utf-8") as f:
        f.write("## Included\n\n")
    try:
        pm.stamp("v1.0.1", body_path)
        check(False, "fail case: a body with no Included entries is refused")
    except SystemExit:
        check(True, "fail case: a body with no Included entries is refused")
finally:
    pm.REPO = saved_repo
    shutil.rmtree(tmp, ignore_errors=True)

print("\n%s" % ("all passed" if not failures else "%d FAILED" % failures))
sys.exit(1 if failures else 0)
