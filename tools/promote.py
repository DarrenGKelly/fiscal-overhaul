"""Builds and checks an autodev -> master promotion (the /promote skill).

    python tools/promote.py plan [--out FILE] [--since PR]
        (this round's PRs, the issues they fix, the next version, the PR body; --since replays from an older promotion)
    python tools/promote.py stamp VERSION --body FILE
        (on the promotion branch: VERSION into ModVersion and UI/mod.json, and a ChangeLog drafted from the body's
        Included list into Mod/Properties/PublishConfiguration.xml, for tools/publish.ps1)
    python tools/promote.py verify PR
        (after the user merged PR: autodev synced, and every issue its listed PRs fix is closed)

A round is the autodev commits since the last promotion PR's head (its headRefOid), not `master..autodev`: the repo
squash-merges, so git sees every past round as missing from master. "Fixes #N" lines in PRs merged into autodev never
close anything (GitHub only acts on merges into the default branch), so the promotion body repeats them.
The promotion branch carries one commit of its own (the stamp), so its head isn't in autodev's history: the body
records the autodev commit it was cut from ("Round head: `<sha>`") and the next round starts there (an older
promotion, without the line, starts at its head).
The first promotion has no earlier one: its round is every autodev commit since autodev left master, and its version
is FIRST_VERSION, or the version autodev's PublishConfiguration.xml already declares when that is higher.
gh finds the repo from this checkout's git remote (or the GH_REPO environment variable), so it needs a GitHub remote
and `gh` signed in. Tests: python tools/test-promote.py
"""

import argparse
import json
from xml.sax.saxutils import escape
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIRST_VERSION = "v0.1.0"
# Promotion branches carry the version (feature/promote-v0-3-11); feature/promote-skill is ordinary work.
PROMOTE_BRANCH_PREFIX = "feature/promote-v"

VERSION_SUBJECT = re.compile(r"^v(\d+)\.(\d+)\.(\d+) - ")
PR_SUFFIX = re.compile(r"\(#(\d+)\)\s*$")
# GitHub's closing keywords, optionally followed by a colon; same-repo references only.
CLOSING = re.compile(r"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?\s+#(\d+)\b", re.IGNORECASE)
# Commits that ride along on autodev but aren't this round's work.
SKIP_SUBJECT = re.compile(r"^Merge ")
ROUND_HEAD = re.compile(r"^Round head: `([0-9a-f]{7,40})`", re.MULTILINE)
CONFIG = os.path.join("Mod", "Properties", "PublishConfiguration.xml")
MOD_JSON = os.path.join("UI", "mod.json")


def round_head(pr):
    """The autodev commit a promotion was cut from: the body's "Round head" line, else the PR's head."""
    match = ROUND_HEAD.search(pr.get("body") or "")
    return match.group(1) if match else pr["headRefOid"]


def is_promotion(pr):
    return pr["headRefName"].startswith(PROMOTE_BRANCH_PREFIX) or "(autodev promotion)" in pr["title"]


def last_promotion(prs, in_autodev=lambda sha: True):
    """The most recently merged promotion PR among merged master PRs, or None.

    A promotion's head was cut from autodev, so a candidate whose head isn't in autodev's history isn't one."""
    promotions = [p for p in prs if p.get("mergedAt") and is_promotion(p) and in_autodev(round_head(p))]
    return max(promotions, key=lambda p: p["mergedAt"]) if promotions else None


def pr_number(subject):
    """The squash-merge PR number: the last "(#N)" at the end of the subject (a subject may cite an issue first)."""
    match = PR_SUFFIX.search(subject)
    return int(match.group(1)) if match else None


def round_entries(subjects):
    """This round's entries from `git log --no-merges --format=%s last..autodev`, oldest first.

    Drops merge commits and master's own version-tagged commits (they arrive through syncs)."""
    entries = []
    for subject in reversed(subjects):
        subject = subject.strip()
        if not subject or SKIP_SUBJECT.match(subject) or VERSION_SUBJECT.match(subject):
            continue
        entries.append({"subject": subject, "pr": pr_number(subject)})
    return entries


INCLUDED_LINE = re.compile(r"^- #(\d+) ", re.MULTILINE)


def included_prs(body):
    """The autodev PR numbers a promotion body lists under "Included"."""
    return [int(n) for n in INCLUDED_LINE.findall(body or "")]


def closing_issues(body):
    """Issue numbers a PR body closes, in order, without duplicates."""
    seen = []
    for number in (int(n) for n in CLOSING.findall(body or "")):
        if number not in seen:
            seen.append(number)
    return seen


def version_key(version):
    """(major, minor, patch) of "v1.2.3" or "1.2.3"; None for anything else."""
    match = re.match(r"^v?(\d+)\.(\d+)\.(\d+)$", (version or "").strip())
    return tuple(int(g) for g in match.groups()) if match else None


def next_version(master_subject, first=False, declared=None, published=()):
    """The next patch after master's latest version-tagged subject ("v0.3.8 - ..." -> "v0.3.9"); FIRST_VERSION for
    the first promotion (master holds no version-tagged commit yet). A higher version autodev already declares (its
    ModVersion, raised by hand for a release) wins. A version already published (a v<version> tag from
    tools/publish.ps1, in published) is never offered again: the next patch after the highest one is."""
    match = VERSION_SUBJECT.match(master_subject)
    if not match and first:
        computed = FIRST_VERSION
    elif not match:
        raise ValueError("not a version-tagged subject: " + master_subject)
    else:
        major, minor, patch = (int(g) for g in match.groups())
        computed = "v%d.%d.%d" % (major, minor, patch + 1)
    if version_key(declared) and version_key(declared) > version_key(computed):
        computed = "v%d.%d.%d" % version_key(declared)
    tags = [version_key(t) for t in published if version_key(t)]
    if tags and max(tags) >= version_key(computed):
        major, minor, patch = max(tags)
        computed = "v%d.%d.%d" % (major, minor, patch + 1)
    return computed


COMMENT = re.compile(r"<!--.*?-->", re.DOTALL)
MOD_VERSION = re.compile(r'(<ModVersion\s+Value=")([^"]*)(")')
CHANGELOG = re.compile(r"<ChangeLog\b[^>]*?/>|<ChangeLog\b[^>]*>.*?</ChangeLog>", re.DOTALL)
MOD_JSON_VERSION = re.compile(r'("version"\s*:\s*")([^"]*)(")')


def _live_matches(pattern, text):
    """pattern's matches in text outside XML comments (the template keeps commented-out examples)."""
    spans = [m.span() for m in COMMENT.finditer(text)]
    return [m for m in pattern.finditer(text) if not any(a <= m.start() < b for a, b in spans)]


def declared_version(config_text):
    """The ModVersion a PublishConfiguration.xml declares, or None."""
    found = _live_matches(MOD_VERSION, config_text or "")
    return found[0].group(2).strip() if found else None


def changelog_from_body(body):
    """A ChangeLog draft from a promotion body: one "- <subject>" line per Included entry."""
    lines, inside = [], False
    for line in (body or "").splitlines():
        if line.startswith("## "):
            inside = line.strip() == "## Included"
        elif inside and line.startswith("- "):
            entry = re.sub(r"^- #\d+ ", "- ", line.rstrip())
            lines.append(re.sub(r" \(direct commit\)$", "", entry))
    return "\n".join(lines)


def _cut(text, match):
    """text without match, and without its line when nothing else is on it."""
    line_start = text.rfind("\n", 0, match.start()) + 1
    line_end = text.find("\n", match.end())
    line_end = len(text) if line_end < 0 else line_end + 1
    if not text[line_start:match.start()].strip() and not text[match.end():line_end].strip():
        return text[:line_start] + text[line_end:]
    return text[:match.start()] + text[match.end():]


def stamp_config(text, version, changelog):
    """PublishConfiguration.xml text with ModVersion set to version and one <ChangeLog> element holding changelog
    (XML-escaped); everything else, comments included, kept as it is."""
    found = _live_matches(MOD_VERSION, text)
    if not found:
        raise ValueError('no <ModVersion Value="..." /> element in ' + CONFIG)
    text = text[:found[0].start(2)] + version + text[found[0].end(2):]
    nl = "\r\n" if "\r\n" in text else "\n"   # the file's own line endings
    block = "<ChangeLog>%s%s%s\t</ChangeLog>" % (nl, nl.join(escape(changelog.strip()).splitlines()), nl)
    logs = _live_matches(CHANGELOG, text)
    if not logs:
        end = text.rindex("</Publish>")
        return text[:end] + "\t" + block + nl + text[end:]
    # ModPublisher keeps the last ChangeLog it reads: leave exactly one.
    for extra in reversed(logs[1:]):
        text = _cut(text, extra)
    return text[:logs[0].start()] + block + text[logs[0].end():]


def stamp_mod_json(text, version):
    """UI/mod.json text with its "version" set; the rest kept as it is."""
    match = MOD_JSON_VERSION.search(text)
    if not match:
        raise ValueError('no "version" in ' + MOD_JSON)
    return text[:match.start(2)] + version + text[match.end(2):]


def render_body(entries, fixes, last, head=None):
    since = "since the last promotion (#%d)" % last["number"] if last else "since it branched from `master` (the first promotion)"
    lines = ["Promotes `autodev` to `master`: everything merged into `autodev` %s." % since, ""]
    if head:
        lines += ["Round head: `%s` (the `autodev` commit this promotion was cut from; the next round starts after it)." % head, ""]
    lines += ["## Included"]
    for entry in entries:
        subject = PR_SUFFIX.sub("", entry["subject"]).rstrip()
        lines.append("- #%d %s" % (entry["pr"], subject) if entry["pr"] else "- %s (direct commit)" % subject)
    lines.append("")
    if fixes:
        lines.append("## Issues closed")
        lines.extend("Fixes #%d" % n for n in fixes)
        lines.append("")
    lines += ["After merging: sync `autodev` with `master`.", "", "\U0001F916 Generated with [Claude Code](https://claude.com/claude-code)"]
    return "\n".join(lines) + "\n"


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True, encoding="utf-8", cwd=REPO).stdout


def gh_json(*args):
    """gh's JSON output; gh resolves the repo from this checkout's remote (or GH_REPO)."""
    return json.loads(run("gh", *args))


def merged_master_prs():
    return gh_json("pr", "list", "--base", "master", "--state", "merged", "--limit", "100",
                   "--json", "number,title,headRefName,headRefOid,mergedAt,body")


def issues_fixed_by(numbers):
    fixes = []
    for number in numbers:
        body = gh_json("pr", "view", str(number), "--json", "body")["body"]
        fixes += [n for n in closing_issues(body) if n not in fixes]
    return fixes


def in_autodev(sha):
    return subprocess.run(["git", "merge-base", "--is-ancestor", sha, "origin/autodev"], capture_output=True,
                          cwd=REPO).returncode == 0


def plan(out, since=None):
    run("git", "fetch", "--prune", "-q")
    synced = in_autodev("origin/master")
    prs = merged_master_prs()
    last = next((p for p in prs if p["number"] == since), None) if since else last_promotion(prs, in_autodev)
    if since and not last:
        sys.exit("#%d isn't a merged master PR" % since)
    if last and not in_autodev(round_head(last)):
        sys.exit("#%d's round head %s isn't in autodev's history, so it can't start a round" % (last["number"], round_head(last)[:9]))
    start = round_head(last) if last else run("git", "merge-base", "origin/master", "origin/autodev").strip()
    head = run("git", "rev-parse", "origin/autodev").strip()
    subjects = run("git", "log", "--no-merges", "--format=%s", start + "..origin/autodev").splitlines()
    entries = round_entries(subjects)
    fixes = issues_fixed_by([e["pr"] for e in entries if e["pr"]])
    try:
        declared = declared_version(run("git", "show", "origin/autodev:" + CONFIG.replace(os.sep, "/")))
    except subprocess.CalledProcessError:
        declared = None
    published = run("git", "ls-remote", "--tags", "--refs", "origin", "v*").split()[1::2]
    version = next_version(run("git", "log", "-1", "--format=%s", "origin/master").strip(), first=last is None,
                           declared=declared, published=[t.rsplit("/", 1)[-1] for t in published])
    body = render_body(entries, fixes, last, head)
    print("autodev synced with master: " + ("yes" if synced else "NO - sync before promoting"))
    if last:
        print("last promotion: #%d (round head %s)" % (last["number"], round_head(last)[:9]))
    else:
        print("last promotion: none (the first promotion: the round starts where autodev left master)")
    print("autodev head:   " + head + " (cut the promotion branch here)")
    print("next version:   " + version + (" (autodev declares ModVersion %s)" % declared if declared else ""))
    print("this round:     %d entries, %d issues" % (len(entries), len(fixes)))
    if out:
        with open(out, "w", encoding="utf-8") as f:
            f.write(body)
        print("body written:   " + out)
    else:
        print("\n" + body)
    return 0 if synced and entries else 1


def stamp(version, body_file):
    """Writes version and the body's ChangeLog draft into this checkout's config and UI/mod.json."""
    if not version_key(version):
        sys.exit("%s isn't a version (vX.Y.Z)" % version)
    bare = "%d.%d.%d" % version_key(version)
    with open(body_file, encoding="utf-8") as f:
        changelog = changelog_from_body(f.read())
    if not changelog:
        sys.exit("%s has no Included entries to draft the ChangeLog from" % body_file)
    config_path, mod_json_path = os.path.join(REPO, CONFIG), os.path.join(REPO, MOD_JSON)
    with open(config_path, encoding="utf-8-sig", newline="") as f:
        config = f.read()
    current = declared_version(config)
    if version_key(current) and version_key(current) > version_key(bare):
        sys.exit("ModVersion is already %s: a release can't go back to %s" % (current, bare))
    with open(mod_json_path, encoding="utf-8", newline="") as f:
        mod_json = f.read()
    with open(config_path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(stamp_config(config, bare, changelog))
    with open(mod_json_path, "w", encoding="utf-8", newline="") as f:
        f.write(stamp_mod_json(mod_json, bare))
    print("stamped %s (ModVersion was %s) into %s and %s, with this ChangeLog:" % (bare, current or "unset", CONFIG, MOD_JSON))
    print(changelog)
    return 0


def verify(number):
    run("git", "fetch", "--prune", "-q")
    pr = gh_json("pr", "view", str(number), "--json", "state,body,baseRefName")
    problems = []
    if pr["baseRefName"] != "master" or pr["state"] != "MERGED":
        problems.append("#%d is %s into %s, not merged into master" % (number, pr["state"], pr["baseRefName"]))
    if subprocess.run(["git", "merge-base", "--is-ancestor", "origin/master", "origin/autodev"], cwd=REPO).returncode != 0:
        problems.append("autodev lacks master's commits: sync it")
    issues = closing_issues(pr["body"])
    issues += [n for n in issues_fixed_by(included_prs(pr["body"])) if n not in issues]
    for issue in issues:
        state = gh_json("issue", "view", str(issue), "--json", "state")["state"]
        if state != "CLOSED":
            problems.append("issue #%d is still %s" % (issue, state))
    for problem in problems:
        print("- " + problem)
    if not problems:
        print("promotion #%d complete: merged, autodev synced, all %d fixed issues closed" % (number, len(issues)))
    return 1 if problems else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("plan")
    p.add_argument("--out")
    p.add_argument("--since", type=int, help="replay the round after this promotion PR instead of the latest")
    s = sub.add_parser("stamp")
    s.add_argument("version", help="the promotion's version, vX.Y.Z")
    s.add_argument("--body", required=True, help="the promotion body from plan --out (its Included list)")
    v = sub.add_parser("verify")
    v.add_argument("pr", type=int)
    args = parser.parse_args(argv)
    if args.command == "plan":
        return plan(args.out, args.since)
    if args.command == "stamp":
        return stamp(args.version, args.body)
    return verify(args.pr)


if __name__ == "__main__":
    sys.exit(main())
