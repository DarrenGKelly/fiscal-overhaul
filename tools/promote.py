"""Builds and checks an autodev -> master promotion (the /promote skill).

    python tools/promote.py plan [--out FILE] [--since PR]
        (this round's PRs, the issues they fix, the next version, the PR body; --since replays from an older promotion)
    python tools/promote.py verify PR
        (after the user merged PR: autodev synced, and every issue its listed PRs fix is closed)

A round is the autodev commits since the last promotion PR's head (its headRefOid), not `master..autodev`: the repo
squash-merges, so git sees every past round as missing from master. "Fixes #N" lines in PRs merged into autodev never
close anything (GitHub only acts on merges into the default branch), so the promotion body repeats them.
The first promotion has no earlier one: its round is every autodev commit since autodev left master, and its version
is FIRST_VERSION.
gh finds the repo from this checkout's git remote (or the GH_REPO environment variable), so it needs a GitHub remote
and `gh` signed in. Tests: python tools/test-promote.py
"""

import argparse
import json
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


def is_promotion(pr):
    return pr["headRefName"].startswith(PROMOTE_BRANCH_PREFIX) or "(autodev promotion)" in pr["title"]


def last_promotion(prs, in_autodev=lambda sha: True):
    """The most recently merged promotion PR among merged master PRs, or None.

    A promotion's head was cut from autodev, so a candidate whose head isn't in autodev's history isn't one."""
    promotions = [p for p in prs if p.get("mergedAt") and is_promotion(p) and in_autodev(p["headRefOid"])]
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


def next_version(master_subject, first=False):
    """The next patch after master's latest version-tagged subject ("v0.3.8 - ..." -> "v0.3.9"); FIRST_VERSION for
    the first promotion (master holds no version-tagged commit yet)."""
    match = VERSION_SUBJECT.match(master_subject)
    if not match and first:
        return FIRST_VERSION
    if not match:
        raise ValueError("not a version-tagged subject: " + master_subject)
    major, minor, patch = (int(g) for g in match.groups())
    return "v%d.%d.%d" % (major, minor, patch + 1)


def render_body(entries, fixes, last):
    since = "since the last promotion (#%d)" % last["number"] if last else "since it branched from `master` (the first promotion)"
    lines = ["Promotes `autodev` to `master`: everything merged into `autodev` %s." % since, "",
             "## Included"]
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
                   "--json", "number,title,headRefName,headRefOid,mergedAt")


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
    if last and not in_autodev(last["headRefOid"]):
        sys.exit("#%d's head %s isn't in autodev's history, so it can't start a round" % (last["number"], last["headRefOid"][:9]))
    start = last["headRefOid"] if last else run("git", "merge-base", "origin/master", "origin/autodev").strip()
    subjects = run("git", "log", "--no-merges", "--format=%s", start + "..origin/autodev").splitlines()
    entries = round_entries(subjects)
    fixes = issues_fixed_by([e["pr"] for e in entries if e["pr"]])
    version = next_version(run("git", "log", "-1", "--format=%s", "origin/master").strip(), first=last is None)
    body = render_body(entries, fixes, last)
    print("autodev synced with master: " + ("yes" if synced else "NO - sync before promoting"))
    if last:
        print("last promotion: #%d (autodev head %s)" % (last["number"], last["headRefOid"][:9]))
    else:
        print("last promotion: none (the first promotion: the round starts where autodev left master)")
    print("next version:   " + version)
    print("this round:     %d entries, %d issues" % (len(entries), len(fixes)))
    if out:
        with open(out, "w", encoding="utf-8") as f:
            f.write(body)
        print("body written:   " + out)
    else:
        print("\n" + body)
    return 0 if synced and entries else 1


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
    v = sub.add_parser("verify")
    v.add_argument("pr", type=int)
    args = parser.parse_args(argv)
    return plan(args.out, args.since) if args.command == "plan" else verify(args.pr)


if __name__ == "__main__":
    sys.exit(main())
