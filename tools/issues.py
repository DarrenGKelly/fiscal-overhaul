"""Sorts the GitHub issue backlog into focus areas and turns one area into an /orchestrate batch.

Every open issue carries exactly one `area:<name>` label (AREAS below is the list), exactly one type (TYPES: bug,
enhancement, tech-debt) and optionally `priority:high` or `priority:low` (no priority label = normal). The filer picks
them (knowledge/issues.md#areas-and-priority); `triage` lists the issues that still need them. Each issue also gets a readiness, from its labels and the issue
template's fields (knowledge/issues.md#readiness): what model can build it, shown on the board and in a batch's README.

    python tools/issues.py areas                     (the area list, for picking a label)
    python tools/issues.py labels [--apply]          (create or update the area and priority labels; dry run without --apply)
    python tools/issues.py board [--area A]          (open issues grouped by area, in batch order; untriaged ones last)
    python tools/issues.py triage                    (open issues without exactly one area and one type; exit 1 if any)
    python tools/issues.py decisions                 (open needs-decision issues, oldest first: the user's queue)
    python tools/issues.py fixed                     (open issues a merged PR says it fixes; exit 1 if one reached master)
    python tools/issues.py batch A [--limit N] [--include-decisions] [--out DIR]
                                                     (write tasks/<date>-<area>/: README.md plus one brief per issue)
gh finds the repo from this checkout's git remote (or the GH_REPO environment variable), so it needs a GitHub remote
and `gh` signed in. Tests: python tools/test-issues.py
"""

import argparse
import datetime
import importlib.util
import json
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AREA_PREFIX = "area:"
AREA_COLOUR = "0e8a16"

# name -> what belongs there. Order is the board's order.
AREAS = {
    "chart": "The Economy panel's time-series line chart: its data series, filters, ranges and rendering",
    "taxation": "Density-based taxation: rates, curves and the C# systems that apply them",
    "ui": "Other UI: panel layout, settings screens, localisation, styles",
    "mod": "Mod plumbing: loading, settings, save data, game-version compatibility, performance",
    "tests": "Test suites (xunit, vitest), flaky or failing tests, test helpers",
    "tooling": "tools/, git hooks, skills, build and deploy",
    "docs": "knowledge/ and CLAUDE.md hygiene",
}

# An issue's type: exactly one, in batch order (bugs first). Docs work is area:docs plus one of these.
TYPES = ("bug", "enhancement", "tech-debt")

PRIORITIES = {
    "priority:high": ("d93f0b", "Do first: fails on autodev, blocks other work, or the user asked for it"),
    "priority:low": ("c5def5", "Do last: nice to have, no current cost"),
}

# The one type label GitHub doesn't create by default (bug and enhancement are built in).
TYPE_LABELS = {
    "tech-debt": ("bfdadc", "Cleanup or restructuring with no visible change"),
}

# Labels that change how an issue is briefed (knowledge/issues.md#labels).
STATUS_LABELS = {
    "needs-decision": ("d876e3", "The fix changes behaviour the user hasn't decided: held back until they do"),
    "needs-plan": ("fbca04", "A feature whose code isn't worked out yet: /plan-feature slices it first"),
    "tracking": ("5319e7", "A planned feature's parent: its slices are the work, so batches skip it"),
}

# readiness -> what it suggests, in the README's legend order.
READINESS = {
    "decision": "held back: ask the user first",
    "tracking": "held back: its slices are the work",
    "plan": "/plan-feature first, or one Opus child for a small one",
    "spelled-out": "Haiku or Sonnet, once you can write the spelled-out lines",
    "open": "Opus",
}
SITE = re.compile(r"[\w./-]+\.(?:cs|ts|tsx|scss|css|py|ps1|sh|md|json|csproj|yaml):\d+")


def has_field(body, name):
    """Whether the issue template's **<name>:** field is present (knowledge/issues.md#the-issue-body)."""
    return re.search(r"\*\*%s\b[^*]*:?\*\*" % re.escape(name), body, re.I) is not None


def readiness(issue):
    """One of READINESS for an issue, from its labels and the template's fields."""
    labels, body = issue["labels"], issue.get("body") or ""
    if "needs-decision" in labels:
        return "decision"
    if "tracking" in labels:
        return "tracking"
    has_site = SITE.search(body) is not None
    if "needs-plan" in labels or ("enhancement" in labels and not has_site):
        return "plan"
    if has_site and has_field(body, "Test") and has_field(body, "Done when"):
        return "spelled-out"
    return "open"


PRIORITY_RANK = {"priority:high": 0, "priority:low": 2}
TYPE_RANK = {name: rank for rank, name in enumerate(TYPES)}


def gh(args, run=subprocess.run):
    """Runs gh with args in the checkout (gh resolves the repo from its remote) and returns stdout; raises on a
    non-zero exit."""
    result = run(["gh"] + args, capture_output=True, text=True, encoding="utf-8", cwd=REPO)
    if result.returncode != 0:
        raise RuntimeError("gh " + " ".join(args) + " failed:\n" + (result.stderr or result.stdout))
    return result.stdout


def fetch_open_issues(run=subprocess.run):
    out = gh(["issue", "list", "--state", "open", "--limit", "1000", "--json", "number,title,labels,body,url,createdAt"], run)
    issues = json.loads(out)
    for issue in issues:
        issue["labels"] = [label["name"] if isinstance(label, dict) else label for label in issue["labels"]]
    return issues


def areas_of(issue):
    return [name[len(AREA_PREFIX):] for name in issue["labels"] if name.startswith(AREA_PREFIX)]


def needs_decision(issue):
    return "needs-decision" in issue["labels"]


def held_back(issue):
    """Issues a batch doesn't brief by default: a decision is open, or the slices are the work."""
    return readiness(issue) in ("decision", "tracking")


def sort_key(issue):
    """Batch order: priority (high, normal, low), then bug before enhancement before tech-debt, then oldest first."""
    priority = min([PRIORITY_RANK[l] for l in issue["labels"] if l in PRIORITY_RANK] or [1])
    kind = min([TYPE_RANK[l] for l in issue["labels"] if l in TYPE_RANK] or [len(TYPE_RANK)])
    return (priority, kind, issue["number"])


def group_by_area(issues):
    """{area: [issues in batch order]} for the known areas, plus "untriaged" for no area, an unknown one, or several."""
    groups = {name: [] for name in AREAS}
    groups["untriaged"] = []
    for issue in issues:
        found = areas_of(issue)
        key = found[0] if len(found) == 1 and found[0] in AREAS else "untriaged"
        groups[key].append(issue)
    for members in groups.values():
        members.sort(key=sort_key)
    return groups


def types_of(issue):
    return [name for name in issue["labels"] if name in TYPES]


def untriaged(issues):
    """(issue, reasons) for each issue without exactly one known area label and exactly one type label."""
    out = []
    for issue in sorted(issues, key=lambda i: i["number"]):
        reasons = []
        found = areas_of(issue)
        if not found:
            reasons.append("no area")
        elif len(found) > 1:
            reasons.append("several areas: " + ", ".join(found))
        elif found[0] not in AREAS:
            reasons.append("unknown area: " + found[0])
        kinds = types_of(issue)
        if not kinds:
            reasons.append("no type: add one of " + ", ".join(TYPES))
        elif len(kinds) > 1:
            reasons.append("several types: " + ", ".join(kinds))
        if reasons:
            out.append((issue, "; ".join(reasons)))
    return out


def decisions(issues):
    """The open needs-decision issues, oldest first: nothing is briefed from them until the user answers."""
    return sorted((i for i in issues if needs_decision(i)), key=lambda i: (i.get("createdAt") or "", i["number"]))


def decisions_text(issues, today):
    queue = decisions(issues)
    lines = ["%d open issues wait on a decision (held back from every batch):" % len(queue), ""]
    for issue in queue:
        created = (issue.get("createdAt") or "")[:10]
        age = "%d days" % (today - datetime.date.fromisoformat(created)).days if created else "age unknown"
        area = (areas_of(issue) or ["no area"])[0]
        lines.append("  #%d %s (%s, %s)" % (issue["number"], issue["title"], age, area))
    return "\n".join(lines).rstrip() + "\n"


def _promote():
    """tools/promote.py, for its closing-keyword parser and how it finds the last promotion."""
    spec = importlib.util.spec_from_file_location("promote", os.path.join(REPO, "tools", "promote.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def fixed_open(issues, prs, promoted):
    """(issue, pr, reached master) for each open issue a merged PR's body closes ("Fixes #N"), by PR number.

    A PR into master has reached it; a PR into autodev has when promoted(pr) says so. An autodev PR's Fixes line only
    closes the issue if the promotion body repeats it, and a hand-edited body can drop it, so the ones on master are issues to close, and the rest close at the next promotion."""
    closing = _promote().closing_issues
    by_number = {i["number"]: i for i in issues}
    out = []
    for pr in sorted(prs, key=lambda p: p["number"]):
        for number in closing(pr.get("body")):
            if number in by_number:
                out.append((by_number[number], pr, pr.get("baseRefName") == "master" or promoted(pr)))
    return out


def merged_prs(run=subprocess.run):
    return json.loads(gh(["pr", "list", "--state", "merged", "--limit", "500",
                          "--json", "number,title,body,baseRefName,mergeCommit"], run))


def promoted_check(run=subprocess.run):
    """A predicate: is this autodev PR's merge commit inside the last promotion's head? Reads the fetched origin refs."""
    promote = _promote()
    masters = json.loads(gh(["pr", "list", "--base", "master", "--state", "merged", "--limit", "100",
                             "--json", "number,title,headRefName,headRefOid,mergedAt"], run))
    last = promote.last_promotion(masters, promote.in_autodev)
    if not last:
        return lambda pr: False

    def check(pr):
        sha = (pr.get("mergeCommit") or {}).get("oid")
        return bool(sha) and run(["git", "merge-base", "--is-ancestor", sha, last["headRefOid"]],
                                 capture_output=True, cwd=REPO).returncode == 0
    return check


def tags(issue):
    shown = [l for l in issue["labels"] if not l.startswith(AREA_PREFIX)]
    return " [" + ", ".join(shown) + "]" if shown else ""


def board_text(issues, only_area=None):
    lines = []
    for area, members in group_by_area(issues).items():
        if only_area and area != only_area:
            continue
        if not members and area != only_area:
            continue
        blurb = AREAS.get(area, "No area label yet: run `python tools/issues.py triage`")
        lines.append("## %s (%d): %s" % (area, len(members), blurb))
        for issue in members:
            lines.append("  #%d %s%s {%s}" % (issue["number"], issue["title"], tags(issue), readiness(issue)))
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def slug(text, limit=48):
    s = re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")
    return s[:limit].rstrip("-") or "issue"


def brief_text(issue):
    body = (issue.get("body") or "").strip() or "(The issue has no body: read the title, then investigate.)"
    labels = ", ".join(issue["labels"]) or "none"
    return (
        "# #%d %s\n\n"
        "Issue: %s\n"
        "Labels: %s\n\n"
        "Readiness: %s (%s).\n\n"
        "Close the issue from the PR: put `Fixes #%d` in its description, or `Part of #%d` when a slice its "
        "**Done when** names is left undone (list it under `Left undone`). "
        "The issue is the test contract: every behaviour it asks for, every bug it reports, and the **Guard** it "
        "names gets a test.\n\n"
        "## The issue\n\n%s\n" % (issue["number"], issue["title"], issue.get("url", ""), labels,
                                   readiness(issue), READINESS[readiness(issue)], issue["number"],
                                   issue["number"], body)
    )


def plan_batch(issues, area, limit=None, include_decisions=False):
    """(chosen, held back for a decision) for one area, in batch order."""
    members = group_by_area(issues).get(area, [])
    hold = (lambda i: readiness(i) == "tracking") if include_decisions else held_back
    held = [i for i in members if hold(i)]
    chosen = [i for i in members if not hold(i)]
    if limit is not None:
        chosen = chosen[:limit]
    return chosen, held


def readme_text(area, chosen, held, files, today):
    lines = [
        "# %s batch: %s" % (area, today),
        "",
        "Built by `python tools/issues.py batch %s` from the open `area:%s` issues. %s." % (area, area, AREAS.get(area, "")),
        "",
        "Order is the backlog's (priority, then bugs first, then oldest), not a dependency order: "
        "build the dependency graph and the file contention map yourself (orchestrate step 1).",
        "",
        "Readiness suggests a tier (knowledge/issues.md#readiness); you pick it when you write the brief: %s." % "; ".join(
            "`%s` %s" % kv for kv in READINESS.items() if kv[0] not in ("decision", "tracking")),
        "",
        "| # | Brief | Issue | Readiness | Labels |",
        "|---|---|---|---|---|",
    ]
    for n, (issue, name) in enumerate(zip(chosen, files), 1):
        lines.append("| %d | [%s](%s) | #%d | %s | %s |" % (
            n, issue["title"].replace("|", "/"), name, issue["number"], readiness(issue),
            ", ".join(l for l in issue["labels"] if not l.startswith(AREA_PREFIX))))
    decisions = [i for i in held if readiness(i) == "decision"]
    tracking = [i for i in held if readiness(i) == "tracking"]
    if decisions:
        lines += ["", "## Held back: needs a decision first", "",
                  "Ask the user about these before briefing them (or rerun with `--include-decisions`):", ""]
        lines += ["- #%d %s" % (i["number"], i["title"]) for i in decisions]
    if tracking:
        lines += ["", "## Held back: tracking issues", "",
                  "Planned features; their slices are briefed instead, and the last slice's PR closes the parent:", ""]
        lines += ["- #%d %s" % (i["number"], i["title"]) for i in tracking]
    return "\n".join(lines) + "\n"


def write_batch(issues, area, out_dir, limit=None, include_decisions=False, today=None):
    """Writes README.md and one NN-<slug>.md per chosen issue into out_dir; returns (chosen, held)."""
    today = today or datetime.date.today().isoformat()
    chosen, held = plan_batch(issues, area, limit, include_decisions)
    if not chosen:
        return chosen, held
    os.makedirs(out_dir, exist_ok=True)
    files = []
    for n, issue in enumerate(chosen, 1):
        name = "%02d-%d-%s.md" % (n, issue["number"], slug(issue["title"]))
        with open(os.path.join(out_dir, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(brief_text(issue))
        files.append(name)
    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(readme_text(area, chosen, held, files, today))
    return chosen, held


def label_commands():
    """The gh label create --force argument lists for every area, non-default type, priority and status label."""
    cmds = [["label", "create", AREA_PREFIX + name, "--color", AREA_COLOUR, "--description", text, "--force"]
            for name, text in AREAS.items()]
    cmds += [["label", "create", name, "--color", colour, "--description", text, "--force"]
             for name, (colour, text) in list(TYPE_LABELS.items()) + list(PRIORITIES.items()) + list(STATUS_LABELS.items())]
    return cmds


def main(argv=None, run=subprocess.run, out=sys.stdout):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("areas")
    labels = sub.add_parser("labels")
    labels.add_argument("--apply", action="store_true")
    board = sub.add_parser("board")
    board.add_argument("--area")
    sub.add_parser("triage")
    sub.add_parser("decisions")
    sub.add_parser("fixed")
    batch = sub.add_parser("batch")
    batch.add_argument("area")
    batch.add_argument("--limit", type=int)
    batch.add_argument("--include-decisions", action="store_true")
    batch.add_argument("--out")
    args = parser.parse_args(argv)

    if args.command == "areas":
        for name, text in AREAS.items():
            out.write("%s%-13s %s\n" % (AREA_PREFIX, name, text))
        for name, (_, text) in PRIORITIES.items():
            out.write("%-18s %s\n" % (name, text))
        return 0

    if args.command == "labels":
        for cmd in label_commands():
            if args.apply:
                gh(cmd, run)
            out.write(("created/updated " if args.apply else "would run: gh ") + " ".join(cmd[:3]) + "\n")
        return 0

    area = getattr(args, "area", None)
    if area and area not in AREAS:
        out.write("Unknown area '%s'. Areas: %s\n" % (area, ", ".join(AREAS)))
        return 2

    issues = fetch_open_issues(run)

    if args.command == "board":
        out.write(board_text(issues, area))
        return 0

    if args.command == "triage":
        missing = untriaged(issues)
        for issue, reason in missing:
            out.write("#%d %s (%s)%s\n" % (issue["number"], issue["title"], reason, tags(issue)))
        out.write("%d of %d open issues need an area or a type.\n" % (len(missing), len(issues)))
        return 1 if missing else 0

    if args.command == "decisions":
        out.write(decisions_text(issues, datetime.date.today()))
        return 0

    if args.command == "fixed":
        found = fixed_open(issues, merged_prs(run), promoted_check(run))
        for issue, pr, reached in found:
            out.write("#%d %s: fixed by #%d (%s)\n" % (
                issue["number"], issue["title"], pr["number"],
                "on master: close it" if reached else "on autodev: closes at the next promotion"))
        stale = sum(1 for _, _, reached in found if reached)
        out.write("%d open issues have a merged fix; %d of them are on master and still open.\n" % (len(found), stale))
        return 1 if stale else 0

    today = datetime.date.today().isoformat()
    out_dir = args.out or os.path.join(REPO, "tasks", "%s-%s" % (today, area))
    chosen, held = write_batch(issues, area, out_dir, args.limit, args.include_decisions, today)
    if not chosen:
        out.write("No open area:%s issues to brief%s.\n" % (area, " (%d held back)" % len(held) if held else ""))
        return 1
    out.write("Wrote %d briefs to %s%s\n" % (len(chosen), out_dir, ", %d held back (see the README)" % len(held) if held else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
