"""Tests tools/okf.py against throwaway bundles, then checks the real knowledge/ bundle.

Each behaviour has a pass case (input the tool accepts or handles) and a fail case (one it must reject or report). Run
after changing okf.py:
    python tools/test-okf.py     (exit 0 = all passed)
"""

import contextlib
import datetime
import importlib.util
import io
import os
import shutil
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("okf", os.path.join(HERE, "okf.py"))
okf = importlib.util.module_from_spec(spec)
spec.loader.exec_module(okf)

failures = 0
NOW = datetime.datetime(2026, 10, 3, 12, 0, tzinfo=datetime.timezone.utc)


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


def parses(text):
    try:
        return okf.parse_frontmatter(text.split("\n")), None
    except okf.FrontmatterError as error:
        return None, str(error)


def doc(kind="Reference", title="A doc", description="What it covers.", extra=""):
    return "---\ntype: %s\ntitle: %s\ndescription: %s\n%s---\n\n# Body\n" % (kind, title, description, extra)


# --- the frontmatter subset ---
data, error = parses('type: Implementation\ntitle: "Quoted: with a colon"\n' "description: 'It''s single'\n"
                     'tags: [ai, "a, b"]\nverified: { by: human:darren, at: 2026-10-03T00:00:00Z }\n# a comment\n'
                     'resource:\n  - Mod/AI\n  - "tools/okf.py"\n'
                     'sources:\n  - id: spec\n    resource: https://example.com/spec\n    title: The spec\n'
                     'verified_list:\n  - { by: process:checker, at: 2026-10-01T00:00:00Z }')
check(error is None, "every supported form parses", error)
if data:
    check(data["title"] == "Quoted: with a colon" and data["description"] == "It's single",
          "double- and single-quoted strings unquote", data)
    check(data["tags"] == ["ai", "a, b"], "a flow list splits on top-level commas only", data["tags"])
    check(data["verified"] == {"by": "human:darren", "at": "2026-10-03T00:00:00Z"}, "a flow map parses", data["verified"])
    check(data["resource"] == ["Mod/AI", "tools/okf.py"], "a block list of scalars parses", data["resource"])
    check(data["sources"] == [{"id": "spec", "resource": "https://example.com/spec", "title": "The spec"}],
          "a block list of maps parses", data["sources"])
    check(data["verified_list"] == [{"by": "process:checker", "at": "2026-10-01T00:00:00Z"}],
          "a block list of flow maps parses", data["verified_list"])

for text, why in [
    ("description: Unquoted: colon", "a plain value with ': ' is refused"),
    ("description: a #hash", "a plain value with ' #' is refused"),
    ("title: [unterminated", "an unterminated flow list is refused"),
    ('title: "unterminated', "an unterminated quote is refused"),
    ("tags: [[nested]]", "nested flow collections are refused"),
    ("executor:\n  resource: x", "a nested block map is refused"),
    ("type: A\ntype: B", "a duplicate key is refused"),
    ("  type: A", "stray indentation is refused"),
    ("just text", "a line that isn't key: value is refused"),
    ("title: *anchor", "a plain value starting with an indicator is refused"),
]:
    data, error = parses(text)
    check(error is not None, why, data)

# --- whole bundles ---
root = tempfile.mkdtemp(prefix="okf-test-")
try:
    bundle = os.path.join(root, "knowledge")
    write(os.path.join(root, "Mod", "Ghost System", "Rig.cs"), "class Rig {}\n")
    write(os.path.join(bundle, "systems.md"), doc(title="Systems", description="How the runtime fits together.",
                                                   extra="resource: Mod\n"))
    write(os.path.join(bundle, "impl", "ghost.md"), doc(
        "Implementation", "Ghost simulation", '"The ghost: a preview."',
        "resource:\n  - Mod/Ghost System\n  - Mod/Ghost System/Rig.cs\n"
        "status: draft\n"
        "verified: { by: human:darren, at: 2026-10-02T09:00:00Z }\n"))
    write(os.path.join(bundle, "repo-structure.md"), doc(title="Repo structure", description="The folder map.")
          + "| `Ghost System/` | The ghost preview |\n")
    write(os.path.join(bundle, "log.md"), "# Update log\n\n## 2026-10-03\n* **Update**: Two\n  wrapped.\n\n"
                                          "## 2026-09-30\n* **Creation**: One.\n")

    def run(*argv):
        out = io.StringIO()
        code = okf.main(list(argv), root, out=out, now=NOW)
        return code, out.getvalue()

    code, out = run("check")
    check(code == 1 and "no index.md" in out, "a bundle without index.md files fails and says to run index", out)
    code, out = run("index")
    check(code == 0 and os.path.exists(os.path.join(bundle, "index.md"))
          and os.path.exists(os.path.join(bundle, "impl", "index.md")), "index writes an index.md per folder", out)
    code, out = run("check")
    check(code == 0, "a conformant bundle passes", out)

    root_index = open(os.path.join(bundle, "index.md"), encoding="utf-8").read()
    check(root_index.startswith('---\nokf_version: "0.2"\n---\n'), "the bundle-root index carries okf_version", root_index)
    check("* [Systems](systems.md) - How the runtime fits together." in root_index,
          "an entry is [Title](link) - description from frontmatter", root_index)
    check("* [impl](impl/index.md) - " + okf.FOLDER_DESCRIPTIONS["impl"] in root_index,
          "a subfolder is listed with its folder description", root_index)
    check("* [Update log](log.md)" in root_index, "the log is listed in its folder's index", root_index)
    impl_index = open(os.path.join(bundle, "impl", "index.md"), encoding="utf-8").read()
    check(not impl_index.startswith("---"), "a subfolder index has no frontmatter", impl_index)
    check("# Implementation\n\n* [Ghost simulation](ghost.md) - The ghost: a preview." in impl_index,
          "entries sit under a heading named after their type", impl_index)
    code, out = run("index")
    check("0 index file(s) updated" in out, "index is stable: a second run changes nothing", out)

    # index drift
    write(os.path.join(bundle, "systems.md"), doc(title="Systems", description="Changed description."))
    code, out = run("check")
    check(code == 1 and "out of date" in out and "+* [Systems](systems.md) - Changed description." in out,
          "an index that no longer matches the frontmatter fails with a diff", out)
    run("index")
    code, out = run("check")
    check(code == 0, "regenerating the index fixes it", out)

    def concept_errors(text, name="probe.md"):
        path = os.path.join(bundle, name)
        write(path, text)
        errors, warnings = okf.check_concept(path, root, NOW)
        os.remove(path)
        return errors, warnings

    errors, _ = concept_errors(doc())
    check(errors == [], "a minimal doc with type, title and description passes", errors)
    errors, _ = concept_errors("# No frontmatter\n")
    check(any("no frontmatter" in e for e in errors), "a doc without frontmatter fails", errors)
    errors, _ = concept_errors(doc(kind="Recipe"))
    check(any("not in the vocabulary" in e for e in errors), "a type outside the vocabulary fails", errors)
    errors, _ = concept_errors("---\ntitle: T\ndescription: D.\n---\n")
    check(any("type is missing" in e for e in errors), "a missing type fails", errors)
    errors, _ = concept_errors("---\ntype: Reference\ntitle: T\n---\n")
    check(any("description is missing" in e for e in errors), "a missing description fails", errors)
    errors, _ = concept_errors(doc(extra="resource: Mod/Gone.cs\n"))
    check(any("Gone.cs" in e and "doesn't exist" in e for e in errors), "a resource path that's gone fails (drift)",
          errors)
    errors, _ = concept_errors(doc(extra="resource: https://example.com/x\n"))
    check(errors == [], "a URL resource isn't checked on disk", errors)
    errors, _ = concept_errors(doc(extra="resource: /systems.md\n"))
    check(errors == [], "a bundle-relative resource resolves against the bundle root", errors)
    errors, _ = concept_errors(doc(extra="status: finished\n"))
    check(any("status" in e for e in errors), "a status outside draft|stable|deprecated fails", errors)
    errors, _ = concept_errors(doc(extra="verified: { by: darren, at: 2026-10-01T00:00:00Z }\n"))
    check(any("actor" in e for e in errors), "a verifier that isn't an actor fails", errors)
    errors, _ = concept_errors(doc(extra="verified: { by: process:okf, at: 2026-10-01 }\n"))
    check(any("ISO 8601" in e for e in errors), "a verified time without a time and offset fails", errors)
    errors, warnings = concept_errors(doc(extra="stale_after: 2026-10-01T00:00:00Z\n"))
    check(errors == [] and any("stale since" in w for w in warnings), "a passed stale_after is a warning, not an error",
          (errors, warnings))
    errors, warnings = concept_errors(doc(extra="stale_after: 2027-01-01T00:00:00Z\n"))
    check(errors == [] and warnings == [], "a future stale_after is quiet", (errors, warnings))
    errors, warnings = concept_errors(doc(extra="generated: { by: claude/opus, at: 2026-10-01T00:00:00Z }\n"))
    check(errors == [] and any("generated" in w for w in warnings), "a hand-kept generated key is warned about",
          (errors, warnings))

    # reserved files
    def index_errors(text, folder="impl"):
        path = os.path.join(bundle, folder, "index.md")
        old = open(path, encoding="utf-8").read()
        write(path, text)
        errors = okf.check_index(path, bundle, expected=False)
        write(path, old)
        return errors

    check(index_errors("# Section\n\n* [Ghost](ghost.md) - The ghost.\n<!-- note -->\n") == [],
          "an index of headings, entries and comments is well-formed")
    errors = index_errors("---\nokf_version: \"0.2\"\n---\n# Section\n")
    check(any("no frontmatter" in e for e in errors), "a subfolder index with frontmatter fails", errors)
    errors = index_errors("# Section\n\nSome prose about the folder.\n")
    check(any("isn't a heading or" in e for e in errors), "prose in an index fails", errors)

    def log_errors(text):
        path = os.path.join(bundle, "log.md")
        old = open(path, encoding="utf-8").read()
        write(path, text)
        errors = okf.check_log(path)
        write(path, old)
        return errors

    check(log_errors("# Log\n\n## 2026-10-03\n* **Update**: x.\n\n## 2026-10-01\n- y.\n") == [],
          "a log with dated headings newest first passes")
    errors = log_errors("# Log\n\n## 2026-10-01\n* a.\n\n## 2026-10-03\n* b.\n")
    check(any("not older" in e for e in errors), "a log out of date order fails", errors)
    errors = log_errors("# Log\n\n## October 3rd\n* a.\n")
    check(any("YYYY-MM-DD" in e for e in errors), "a log heading that isn't an ISO date fails", errors)
    errors = log_errors("# Log\n\n## 2026-10-03\nProse, not a bullet.\n")
    check(any("bullets" in e for e in errors), "a log entry that isn't a bullet fails", errors)
    errors = log_errors("# Log\n\n## 2026-10-03\n* **Update**: same.\n* **Update**: other.\n\n## 2026-10-01\n"
                        "* **Update**: same.\n")
    check(any("line 8: repeats the entry on line 4" in e for e in errors), "a bullet repeated in a log (a union merge) fails",
          errors)

    # the log's monthly archive
    def archive_errors(text, name="2026-09.md"):
        path = os.path.join(bundle, "log", name)
        write(path, text)
        errors = okf.check_log(path)
        os.remove(path)
        return errors

    check(archive_errors("# Update log, 2026-09\n\n## 2026-09-30\n* a.\n\n## 2026-09-02\n* b.\n") == [],
          "an archive month holding only its own dates passes")
    errors = archive_errors("# Update log, 2026-09\n\n## 2026-10-01\n* a.\n")
    check(any("doesn't belong in the 2026-09 archive" in e for e in errors), "an archive holding another month fails",
          errors)
    errors = archive_errors("# Update log\n\n## 2026-09-30\n* a.\n", name="old.md")
    check(any("YYYY-MM.md" in e for e in errors), "a log/ file not named YYYY-MM.md fails", errors)
    os.rmdir(os.path.join(bundle, "log"))
    path = os.path.join(bundle, "log", "2026-09.md")
    write(path, "# Update log, 2026-09\n\n## 2026-09-30\n* " + "x" * okf.DOC_BUDGET + "\n")
    check(okf.check_budget(path, root) == [], "an archive month has no size budget", okf.check_budget(path, root))
    run("index")
    root_index = open(os.path.join(bundle, "index.md"), encoding="utf-8").read()
    check("* [Update log, 2026-09](log/2026-09.md) - " in root_index, "the index lists each archive month", root_index)
    check(not os.path.exists(os.path.join(bundle, "log", "index.md")), "the archive folder gets no index of its own")
    code, out = run("check")
    check(code == 0 and "knowledge/log/2026-09.md" not in out, "a bundle with an archive passes check", out)
    os.remove(path)
    os.rmdir(os.path.join(bundle, "log"))
    run("index")
    log_path = os.path.join(bundle, "log.md")
    old_log = open(log_path, encoding="utf-8").read()
    write(log_path, "x" * (okf.DOC_BUDGET + 1))
    errors = okf.check_budget(log_path, root)
    check(any("rotate-log" in e for e in errors), "a full log.md says to run rotate-log", errors)
    write(log_path, old_log)
    check(okf.check_budget(log_path, root) == [], "control: a log.md within budget passes")

    # size budgets
    def budget_errors(size, name="probe.md", allowlist=None):
        path = os.path.join(bundle, name)
        write(path, "x" * size)
        errors = okf.check_budget(path, root, allowlist)
        os.remove(path)
        return errors

    check(budget_errors(okf.DOC_BUDGET) == [], "a doc at its 40 KB budget passes")
    errors = budget_errors(okf.DOC_BUDGET + 1)
    check(any("over the 40 KB budget" in e for e in errors), "a doc one byte over its budget fails", errors)
    check(budget_errors(okf.IMPL_BUDGET, "impl/probe.md") == [], "an impl doc at its 35 KB budget passes")
    errors = budget_errors(okf.IMPL_BUDGET + 1, "impl/probe.md")
    check(any("over the 35 KB budget" in e for e in errors), "an impl doc over its smaller budget fails", errors)
    # an allowlisted file's cap: its size when listed +5%, rounded up to the next KB
    check(okf.allowlist_cap(100 * okf.KB) == 105 * okf.KB, "a 100 KB file's cap is 105 KB (5% headroom)",
          okf.allowlist_cap(100 * okf.KB))
    check(okf.allowlist_cap(100 * okf.KB + 1) == 106 * okf.KB, "a cap rounds up to the next KB",
          okf.allowlist_cap(100 * okf.KB + 1))
    check(all(cap % okf.KB == 0 for cap in okf.BUDGET_ALLOWLIST.values()), "every allowlisted cap is whole KB")
    listed = okf.DOC_BUDGET + 2000
    cap = {"knowledge/probe.md": okf.allowlist_cap(listed)}
    check(budget_errors(listed + 1500, allowlist=cap) == [], "an allowlisted doc that grows within its headroom passes")
    check(budget_errors(cap["knowledge/probe.md"], allowlist=cap) == [], "an allowlisted oversized doc at its cap passes")
    errors = budget_errors(cap["knowledge/probe.md"] + 1, allowlist=cap)
    check(any("allowlisted cap" in e and "may not grow" in e for e in errors),
          "an allowlisted doc that grows past its cap fails", errors)
    # the allowlist near-cap warning: within ALLOWLIST_NEAR (512) bytes of its cap, an allowlisted doc warns "split it"
    capped = cap["knowledge/probe.md"]
    check(okf.ALLOWLIST_NEAR == 512, "the allowlist near-cap margin is 512 bytes", okf.ALLOWLIST_NEAR)
    errors, notes = okf.budget_problems("knowledge/probe.md", capped - 100, cap)
    check(errors == [] and len(notes) == 1 and "split it" in notes[0] and "within 512 bytes" in notes[0],
          "an allowlisted doc 100 bytes under its cap warns to split it", (errors, notes))
    errors, notes = okf.budget_problems("knowledge/probe.md", capped - 512, cap)
    check(errors == [] and len(notes) == 1, "an allowlisted doc exactly 512 bytes under its cap warns", (errors, notes))
    check(okf.budget_problems("knowledge/probe.md", capped - 513, cap) == ([], []),
          "control: an allowlisted doc 513 bytes under its cap has no warning")
    check(okf.budget_problems("knowledge/probe.md", capped - 2000, cap) == ([], []),
          "control: an allowlisted doc further from its cap has no warning")
    errors, notes = okf.budget_problems("knowledge/probe.md", okf.DOC_BUDGET - 100)
    check(errors == [] and notes == [], "control: a doc near its default budget but not allowlisted gets no "
          "allowlist warning (that one is the staged NEAR_CAP rule)", (errors, notes))
    # the ratchet: a cap only goes down
    big = {"knowledge/probe.md": okf.allowlist_cap(60 * okf.KB)}
    errors = budget_errors(55 * okf.KB, allowlist=big)
    check(any("lower it to 58 KB" in e for e in errors), "a shrunk allowlisted doc whose cap is over 1 KB loose fails",
          errors)
    check(budget_errors(60 * okf.KB - 500, allowlist=big) == [], "control: a doc shrunk by less than the slack passes")
    errors = budget_errors(okf.DOC_BUDGET - 10, allowlist=cap)
    check(any("fits the default" in e and "delete" in e for e in errors),
          "an allowlisted doc that fits its default budget fails", errors)
    errors = budget_errors(okf.DOC_BUDGET - 10)
    check(errors == [], "control: the same doc not allowlisted passes", errors)
    impl_cap = {"knowledge/impl/probe.md": 40 * okf.KB}
    errors = budget_errors(okf.IMPL_BUDGET - 10, "impl/probe.md", allowlist=impl_cap)
    check(any("fits the default 35 KB" in e for e in errors), "the ratchet uses the impl budget for impl docs", errors)
    for name, listed_cap in okf.BUDGET_ALLOWLIST.items():
        path = os.path.join(okf.REPO, name)
        if os.path.exists(path):
            check(okf.check_ratchet(name, okf.doc_size(path), listed_cap) == [],
                  "the real allowlist is tight: " + name)
    # CLAUDE.md's budget
    claude = os.path.join(root, "CLAUDE.md")
    budget = 19 * okf.KB
    write(claude, "x" * budget)
    check(okf.check_claude_md(root, budget) == [], "a CLAUDE.md at its budget passes")
    write(claude, "x" * (budget - 4 * okf.KB))
    check(okf.check_claude_md(root, budget) == [], "a CLAUDE.md well under its budget passes (no ratchet down)")
    write(claude, "x" * (budget + okf.KB // 2))
    check(okf.check_claude_md(root, budget) != [], "a CLAUDE.md 0.5 KB over its budget fails")
    write(claude, "x" * (budget + 1))
    errors = okf.check_claude_md(root, budget)
    check(any("over its 19 KB budget" in e for e in errors), "a CLAUDE.md one byte over its budget fails", errors)
    os.remove(claude)
    check(okf.check_claude_md(root) == [], "a checkout with no CLAUDE.md has nothing to check")
    path = os.path.join(okf.REPO, okf.CLAUDE_MD)
    check(okf.doc_size(path) <= okf.CLAUDE_MD_BUDGET, "the real CLAUDE.md fits its budget", okf.doc_size(path))

    # dated history in rule docs
    def dated(text, name="probe.md"):
        path = os.path.join(bundle, name)
        write(path, text)
        result = okf.check_dated_history(path, root)
        os.remove(path)
        return result

    diary = doc() + "Decided (user, 2026-10-05): the arrow is white.\n"
    errors, notes = dated(diary)
    check(len(errors) == 1 and notes == [] and "line 8" in errors[0], "a doc with '(user, 2026-10-05)' is an error",
          (errors, notes))
    check(dated(doc() + "The arrow is white.\n") == ([], []), "a clean doc gets no warning or error")
    for line, why in [("Before 2026-10-05 every ring was white.\n", "a 'Before <date>' sentence"),
                      ("`secondsAhead` 1 → 0.25 on 2026-10-05.\n", "a value history (arrow and date)")]:
        errors, _ = dated(doc() + line)
        check(len(errors) == 1, why + " is dated history", errors)
    for line, why in [("`secondsAhead` 1 → 0.25.\n", "an arrow with no date"),
                      ("See the 2026-10-05 audit.\n", "a bare date"),
                      ("```\n(user, 2026-10-05)\n```\n", "a fenced code block")]:
        check(dated(doc() + line) == ([], []), why + " isn't dated history")
    for line in ["The user 2026-10-02 chose white.\n", "Chosen by the user, 2026-10-02.\n", "Decided 2026-10-01 to drop it.\n",
                 "It was decided on 2026-10-01.\n", "It was removed on 2026-10-07 for good.\n",
                 "The ring was added on 2026-10-07.\n", "Width changed on 2026-10-07.\n",
                 "**Rates stay** (the user's decision, 2026-09-22; the brief said nothing).\n",
                 "The tax curve is per zone (2026-10-03, the user: +50 %).\n",
                 "User's decision (2026-10-01, playtest item 12): bars stay.\n",
                 "Headroom 1.5 (user decision 2026-10-02 after a playtest).\n",
                 "Live entries (playtest 2026-10-01: the chart went stale).\n",
                 "Lines outlining text (fixed 2026-10-02).\n",
                 "2026-10-18: the line-chart rewrite.\n",
                 "Part of the run (user decision\n2026-10-18: the rewrite).\n"]:
        errors, _ = dated(doc() + line)
        check(len(errors) == 1, "dated narrative is an error: " + line.strip(), errors)
    for line, why in [("A playtest found the chart went stale.\n", "a playtest with no date"),
                      ("The 2026-10-18 audit: see its summary.\n", "a date that doesn't open the line with a colon")]:
        check(dated(doc() + line) == ([], []), why + " isn't dated history")
    # history by issue number: a warning only
    errors, notes = dated(doc() + "Until #418 each state wrote the enum by hand.\n")
    check(errors == [] and len(notes) == 1 and "issue number" in notes[0], "'Until #418' warns, never errors",
          (errors, notes))
    check(dated(doc() + "Each state writes the enum (#418).\n") == ([], []), "an issue number alone doesn't warn")
    check(dated(doc() + "The ring is no longer white.\n") == ([], []), "'no longer' alone is not dated history")
    # the size line compares against origin/master; the born-above-70% warning
    probe_path = os.path.join(bundle, "probe.md")
    write(probe_path, doc())
    line = okf.bundle_totals([probe_path], root, {"knowledge/probe.md": 1})
    check("KB since the last promotion" in line, "the size line quotes the delta vs master", line)
    line = okf.bundle_totals([probe_path], root, None)
    check("since the last promotion" not in line, "no master ref: no delta, no crash", line)
    check(okf.master_sizes(root) is None, "a directory with no git history has no master sizes")
    os.remove(probe_path)
    impl_path = "knowledge/impl/probe.md"
    check(okf.born_budget_warnings(impl_path, int(0.75 * okf.IMPL_BUDGET), {}) != [], "a new impl doc at 75% warns")
    check(okf.born_budget_warnings(impl_path, int(0.60 * okf.IMPL_BUDGET), {}) == [], "a new impl doc at 60% doesn't")
    check(okf.born_budget_warnings(impl_path, int(0.75 * okf.IMPL_BUDGET), {impl_path: 1}) == [],
          "a doc already on master doesn't warn as born")
    check(okf.born_budget_warnings(impl_path, int(0.75 * okf.IMPL_BUDGET), None) == [], "no master ref: no warning")
    check(dated(diary, "probe/log.md") == ([], []), "log.md is exempt from the dated-history lint")
    check(dated(diary, "log/2026-09.md") == ([], []), "the log archive is exempt from the dated-history lint")
    path = os.path.join(bundle, "crlf.md")
    with open(path, "wb") as handle:
        handle.write(b"x\r\n" * (okf.DOC_BUDGET // 2))
    check(okf.check_budget(path, root) == [], "size is measured with LF endings (a CRLF checkout measures the same)")
    os.remove(path)
    path = os.path.join(bundle, "big.md")
    write(path, doc(title="Big", description="Too big.") + "x" * okf.DOC_BUDGET)
    code, out = run("check", "knowledge/big.md")
    check(code == 1 and "ERROR knowledge/big.md" in out and "budget" in out, "check fails a doc over its budget", out)
    # merge-safe budgets: with --staged (the pre-commit hook) an unstaged over-budget doc warns; a staged one errors
    staged_list = os.path.join(root, "staged.txt")
    write(staged_list, "knowledge/systems.md\n")
    code, out = run("check", "--staged", staged_list, "knowledge/big.md")
    check(code == 0 and "WARN  knowledge/big.md: [budget] not staged in this commit" in out,
          "--staged: an over-budget doc the commit doesn't stage only warns, naming it", out)
    write(staged_list, "knowledge/big.md\n")
    code, out = run("check", "--staged", staged_list, "knowledge/big.md")
    check(code == 1 and "ERROR knowledge/big.md: [budget]" in out, "--staged: a staged over-budget doc fails", out)
    os.remove(path)
    # the near-cap warning: a staged doc within 2% of its budget
    write(path, doc(title="Big", description="Nearly full.") + "x" * (okf.DOC_BUDGET * 99 // 100 - len(doc())))
    code, out = run("check", "--staged", staged_list, "knowledge/big.md")
    check(code == 0 and "WARN  knowledge/big.md: [budget]" in out and "within 2%" in out,
          "a staged doc at 99% of its budget warns", out)
    code, out = run("check", "knowledge/big.md")
    check(code == 0 and "within 2%" not in out, "control: without --staged the near-cap warning stays quiet", out)
    write(path, doc(title="Big", description="Room to spare.") + "x" * (okf.DOC_BUDGET * 9 // 10))
    code, out = run("check", "--staged", staged_list, "knowledge/big.md")
    check(code == 0 and "within 2%" not in out, "control: a staged doc at 90% of its budget doesn't warn", out)
    os.remove(path)
    os.remove(staged_list)
    check(okf.near_cap_warnings("knowledge/probe.md", okf.DOC_BUDGET) != [], "a doc exactly at its budget is near cap")
    check(okf.near_cap_warnings("knowledge/probe.md", okf.DOC_BUDGET + 1) == [],
          "control: a doc over its budget gets the error, not the near-cap warning")
    # a generated index.md is never refused by a check; above INDEX_BUDGET it warns "compact it", not "split the
    # folder": impl/ stays flat
    errors, notes = okf.budget_problems("knowledge/impl/index.md", okf.INDEX_BUDGET + 1)
    check(errors == [] and len(notes) == 1 and "compact it" in notes[0] and "impl/ stays flat" in notes[0]
          and "split the folder" not in notes[0],
          "a generated index over INDEX_BUDGET passes the budget check and warns to compact it", (errors, notes))
    check(okf.budget_problems("knowledge/impl/index.md", okf.INDEX_BUDGET) == ([], []),
          "control: an index at INDEX_BUDGET has no warning")
    errors, notes = okf.budget_problems("knowledge/impl/probe.md", 36 * okf.KB)
    check(len(errors) == 1 and "over the 35 KB budget" in errors[0], "a 36 KB impl concept doc errors", errors)
    for name in okf.BUDGET_ALLOWLIST:
        check(os.path.exists(os.path.join(okf.REPO, name)), "allowlisted file exists: " + name)

    # the folder map
    check(okf.check_folder_map(root) == [], "a code folder named in repo-structure.md passes")
    write(os.path.join(root, "UI", "src", "charts", "Line.tsx"), "export {}\n")
    errors = okf.check_folder_map(root)
    check(len(errors) == 1 and "UI/src/charts/" in errors[0],
          "a top-level code folder missing from repo-structure.md fails", errors)
    code, out = run("check")
    check(code == 1 and "UI/src/charts/" in out, "a whole-bundle check runs the folder map", out)
    code, out = run("check", "knowledge/impl")
    check(code == 0, "a check of named paths skips the whole-bundle folder map", out)
    shutil.rmtree(os.path.join(root, "UI"))
    for skipped in ("bin", "obj"):
        write(os.path.join(root, "Mod", skipped, "Release", "x.dll"), "x\n")
    check(okf.check_folder_map(root) == [], "build output (Mod/bin/, Mod/obj/) isn't a code folder to map",
          okf.check_folder_map(root))

    code, out = run("check", "knowledge/impl")
    check(code == 0 and "file(s) checked" in out, "check takes a folder inside the bundle", out)
    try:
        run("check", "Mod")
        check(False, "a path outside the bundle is refused")
    except SystemExit as stop:
        check("outside the bundle" in str(stop), "a path outside the bundle is refused", stop)
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- rotate-log: moving the log's oldest days into log/YYYY-MM.md ---
root = tempfile.mkdtemp(prefix="okf-rotate-")
try:
    bundle = os.path.join(root, "knowledge")
    TODAY = datetime.datetime(2026, 10, 5, 9, 0, tzinfo=datetime.timezone.utc)
    day = lambda date, *bullets: "## %s\n" % date + "".join("* **Update**: %s\n" % b for b in bullets)
    log_text = ("# Update log\n\n" + day("2026-10-05", "today " + "t" * 300) + "\n" + day("2026-10-04", "four")
                + "\n" + day("2026-10-03", "three", "three b") + "  wrapped line.\n\n" + day("2026-09-30", "thirty"))
    log_text = log_text.replace("thirty", "thirty ([doc](impl/a.md#x), [web](https://example.com/a.md), [here](#y))")
    write(os.path.join(bundle, "log.md"), log_text)
    write(os.path.join(bundle, "log", "2026-10.md"), "# Update log, 2026-10\n\n" + day("2026-10-03", "older three")
          + "\n" + day("2026-10-01", "one"))
    write(os.path.join(bundle, "impl", "a.md"), "[why](../log.md#2026-09-30), [kept](../log.md#2026-10-05), "
                                                "[other](../other/log.md#2026-09-30)\n")
    write(os.path.join(root, "CLAUDE.md"), "[log](knowledge/log.md#2026-10-03)\n")

    def lines_of(*paths):
        return [l for p in paths for l in open(os.path.join(bundle, p), encoding="utf-8").read().splitlines()
                if l.strip() and not l.startswith("# ")]

    before = lines_of("log.md", "log/2026-10.md")
    out = io.StringIO()
    moved = okf.rotate_log(root, now=TODAY, target=100, out=out)
    check(moved == ["2026-10-04", "2026-10-03", "2026-09-30"], "rotate-log moves the oldest whole days, never today's",
          (moved, out.getvalue()))
    log_after = open(os.path.join(bundle, "log.md"), encoding="utf-8").read()
    check(log_after == "# Update log\n\n" + day("2026-10-05", "today " + "t" * 300), "today's entries stay in log.md",
          log_after)
    october = open(os.path.join(bundle, "log", "2026-10.md"), encoding="utf-8").read()
    check(october == "# Update log, 2026-10\n\n" + day("2026-10-04", "four") + "\n"
          + day("2026-10-03", "three", "three b") + "  wrapped line.\n* **Update**: older three\n\n"
          + day("2026-10-01", "one"),
          "moved days merge into an existing month newest first, a day already there keeping its bullets below",
          october)
    september = open(os.path.join(bundle, "log", "2026-09.md"), encoding="utf-8").read()
    check(september.startswith("# Update log, 2026-09\n\n## 2026-09-30\n* **Update**: thirty"),
          "a new month's archive is created", september)
    check("([doc](../impl/a.md#x), [web](https://example.com/a.md), [here](#y))" in september,
          "a moved entry's relative links are rebased onto the archive folder; URLs and bare anchors are kept",
          september)
    after = [l.replace("](../impl/", "](impl/") for l in lines_of("log.md", "log/2026-10.md", "log/2026-09.md")]
    check(sorted(before) == sorted(after + ["## 2026-10-03"]),
          "no line is lost (the merged day's duplicate heading aside)", (before, after))
    for name in ("log.md", "log/2026-10.md", "log/2026-09.md"):
        check(okf.check_log(os.path.join(bundle, name)) == [], "%s passes check after rotating" % name)
    linked = open(os.path.join(bundle, "impl", "a.md"), encoding="utf-8").read()
    check("[why](../log/2026-09.md#2026-09-30)" in linked, "a link to a moved day is repointed at its month", linked)
    check("[kept](../log.md#2026-10-05)" in linked and "[other](../other/log.md#2026-09-30)" in linked,
          "links to a day still in log.md, or to another folder's log, are left alone", linked)
    claude = open(os.path.join(root, "CLAUDE.md"), encoding="utf-8").read()
    check(claude == "[log](knowledge/log/2026-10.md#2026-10-03)\n", "links from CLAUDE.md are repointed too", claude)
    out = io.StringIO()
    moved = okf.rotate_log(root, now=TODAY, target=100, out=out)
    check(moved == [] and "nothing to move" in out.getvalue(), "a log under its target is left alone", out.getvalue())
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- rotate-log when the clock's date is past the log's newest day ---
root = tempfile.mkdtemp(prefix="okf-rotate-utc-")
try:
    bundle = os.path.join(root, "knowledge")
    day = lambda date, *bullets: "## %s\n" % date + "".join("* **Update**: %s\n" % b for b in bullets)
    newest = day("2026-10-05", "local today " + "t" * 300)
    write(os.path.join(bundle, "log.md"), "# Update log\n\n" + newest + "\n" + day("2026-10-04", "yesterday"))
    TOMORROW_UTC = datetime.datetime(2026, 10, 6, 1, 0, tzinfo=datetime.timezone.utc)
    out = io.StringIO()
    moved = okf.rotate_log(root, now=TOMORROW_UTC, target=100, out=out)
    log_after = open(os.path.join(bundle, "log.md"), encoding="utf-8").read()
    check(moved == ["2026-10-04"] and log_after == "# Update log\n\n" + newest,
          "a clock date past the newest entry keeps the newest day in log.md (it never empties it)",
          (moved, log_after))
    check(okf.check_log(os.path.join(bundle, "log.md")) == [], "the rotated log.md still passes check")
    write(os.path.join(bundle, "log.md"), "# Update log\n\n" + newest + "\n" + day("2026-10-04", "yesterday"))
    shutil.rmtree(os.path.join(bundle, "log"))
    moved = okf.rotate_log(root, now=datetime.datetime(2026, 10, 5, 23, 0), target=100, out=io.StringIO())
    check(moved == ["2026-10-04"], "a naive local now still moves the days before it", moved)
    write(os.path.join(bundle, "log.md"), "# Update log\n\n" + newest)
    moved = okf.rotate_log(root, now=TOMORROW_UTC, target=100, out=io.StringIO())
    check(moved == [] and "local today" in open(os.path.join(bundle, "log.md"), encoding="utf-8").read(),
          "a log holding only one day is never emptied, however far the clock is ahead", moved)
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- section: one heading's section of a doc, not the whole doc ---
root = tempfile.mkdtemp(prefix="okf-section-")
try:
    path = os.path.join(root, "knowledge", "big.md")
    write(path, doc(title="Big", description="A long doc.") + "\n# Big\n\nIntro.\n\n## Units\n\nUnit text.\n\n"
          "### The `Bombard`\n\nBombard text.\n\n```\n# not a heading\n## nor this\n```\n\n#### Deeper\n\nDeep.\n\n"
          "### Notes\n\nNotes text.\n\n## Weapons\n\nWeapon text.\n\n## Units\n\nSecond units.\n")

    def run_section(target):
        out = io.StringIO()
        code = okf.main(["section", target], root, out=out, now=NOW)
        return code, out.getvalue()

    code, out = run_section("knowledge/big.md#the-bombard")
    check(code == 0 and out == "### The `Bombard`\n\nBombard text.\n\n```\n# not a heading\n## nor this\n```\n\n"
          "#### Deeper\n\nDeep.\n", "an anchor prints only its section: deeper headings and fenced code stay in, "
          "the next same-level heading ends it", out)
    code, out = run_section("knowledge/big.md#units")
    check(code == 0 and out.startswith("## Units\n\nUnit text.") and out.endswith("Notes text.\n")
          and "Weapon" not in out and "Intro" not in out, "a section runs to the next heading of a higher level", out)
    code, out = run_section("knowledge/big.md#units-1")
    check(code == 0 and out == "## Units\n\nSecond units.\n", "a repeated heading's -1 anchor picks the second one, "
          "and the last section runs to the end of the doc", out)
    code, out = run_section(path + "#weapons")
    check(code == 0 and out == "## Weapons\n\nWeapon text.\n", "an absolute path works too", out)
    code, out = run_section("knowledge/big.md#missing")
    check(code == 1 and "no heading with anchor #missing" in out, "a missing anchor exits non-zero", out)
    code, out = run_section("knowledge/absent.md#units")
    check(code == 1 and "no such doc" in out, "a missing doc exits non-zero", out)
    code, out = run_section("knowledge/not-a-heading")
    check(code == 2, "a target without an anchor is refused", out)
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- the how-to-extend index: one source (patterns.md), a generated copy in CLAUDE.md ---
root = tempfile.mkdtemp(prefix="okf-extend-")
try:
    bundle = os.path.join(root, "knowledge")
    write(os.path.join(bundle, "repo-structure.md"), doc(title="Repo structure", description="The folder map."))
    write(os.path.join(bundle, "patterns.md"), doc(title="Patterns", description="Build on this.")
          + "\n## Start here\n\n<!-- how-to-extend:start -->\n| Task | Recipe |\n|---|---|\n"
            "| Add a panel | [ui.md](ui.md#adding-a-panel), [below](#tests) |\n<!-- how-to-extend:end -->\n\n## Tests\n")
    claude_path = os.path.join(root, "CLAUDE.md")
    with open(claude_path, "w", encoding="utf-8", newline="\r\n") as handle:   # a CRLF checkout keeps its endings
        handle.write("# Project\n\nIntro.\n\n<!-- how-to-extend:start -->\n| stale |\n<!-- how-to-extend:end -->\n\nTail.\n")

    def run(*argv):
        out = io.StringIO()
        code = okf.main(list(argv), root, out=out, now=NOW)
        return code, out.getvalue()

    code, out = run("index", "knowledge")
    check("how-to-extend" not in out and "| stale |" in open(claude_path, encoding="utf-8").read(),
          "index of a folder writes only the folder indexes, not the how-to-extend copy", out)
    code, out = run("check")
    check(code == 1 and "ERROR CLAUDE.md" in out and "how-to-extend" in out, "a stale how-to-extend copy fails check",
          out)
    code, out = run("index")
    check(code == 0, "index runs on a bundle with a how-to-extend source", out)
    claude = open(claude_path, encoding="utf-8").read()
    check("| Add a panel | [ui.md](knowledge/ui.md#adding-a-panel), [below](knowledge/patterns.md#tests) |" in claude
          and claude.startswith("# Project\n\nIntro.\n\n" + okf.EXTEND_COPY_START + "\n| Task | Recipe |")
          and claude.endswith("<!-- how-to-extend:end -->\n\nTail.\n") and "| stale |" not in claude,
          "index writes the source table between CLAUDE.md's markers, links rebased and own anchors made absolute",
          claude)
    check(b"\r\n" in open(claude_path, "rb").read(), "index keeps a CRLF copy's line endings")
    code, out = run("check")
    check(code == 0, "a generated copy passes check", out)
    code, out = run("index")
    check("how-to-extend" not in out, "index is stable: an up-to-date copy isn't rewritten", out)
    hand_edited = open(claude_path, encoding="utf-8").read().replace("| Add a panel |", "| Add a UI panel |")
    write(claude_path, hand_edited)
    code, out = run("check")
    check(code == 1 and "ERROR CLAUDE.md" in out and "differs from knowledge/patterns.md" in out,
          "a hand-edited copy fails check and names the source to edit", out)
    write(claude_path, "# Project\n\nNo markers.\n")
    code, out = run("check")
    check(code == 1 and "needs the generated how-to-extend block" in out, "a copy with its markers removed fails", out)
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- budget-tree: the size budgets on a git tree (land-pr checks the merge of a PR with origin/autodev) ---
import subprocess
root = tempfile.mkdtemp(prefix="okf-tree-")
try:
    def git(*argv):
        return subprocess.run(["git", "-C", root] + list(argv), capture_output=True, text=True, check=True).stdout.strip()

    def tree_with(files):
        for name, size in files.items():
            write(os.path.join(root, name), "x" * size)
        git("add", "--all")
        return git("write-tree")

    def run_tree(tree):
        out = io.StringIO()
        return okf.main(["budget-tree", tree], root, out=out), out.getvalue()

    git("init", "--quiet")
    code, out = run_tree(tree_with({"knowledge/impl/probe.md": 34 * okf.KB, "knowledge/impl/index.md": okf.INDEX_BUDGET + okf.KB,
                                    "knowledge/log/2026-09.md": 90 * okf.KB, "notes/elsewhere.md": 90 * okf.KB}))
    check(code == 0 and "WARN  knowledge/impl/index.md: [budget]" in out and "elsewhere" not in out,
          "budget-tree passes a tree that fits (an index over its budget warns; archives and other folders are skipped)",
          out)
    code, out = run_tree(tree_with({"knowledge/impl/probe.md": 36 * okf.KB}))
    check(code == 1 and "ERROR knowledge/impl/probe.md: [budget]" in out, "budget-tree fails a doc over budget", out)
    code, out = run_tree(tree_with({"knowledge/impl/probe.md": 1, ".claude/skills/x/SKILL.md": okf.SKILL_BUDGET + 1}))
    check(code == 1 and "ERROR .claude/skills/x/SKILL.md" in out, "budget-tree covers the skills' budget", out)
    code, out = run_tree("no-such-tree")
    check(code == 2, "budget-tree on a bad tree-ish exits 2", out)
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- the jam list and the grow-into-the-cap error: impl/ docs against the 35,840-byte IMPL_BUDGET ---
root = tempfile.mkdtemp(prefix="okf-jam-")
try:
    def git(*argv):
        return subprocess.run(["git", "-C", root, "-c", "user.name=okf-test", "-c", "user.email=okf@test"]
                              + list(argv), capture_output=True, text=True, check=True).stdout.strip()

    impl = os.path.join(root, "knowledge", "impl")
    probe = "knowledge/impl/probe.md"

    def sized(name, size, title="Probe"):
        head = doc("Implementation", title, "A doc of a set size.")
        write(os.path.join(root, name), head + "x" * (size - len(head) - 1) + "\n")
        check(okf.doc_size(os.path.join(root, name)) == size, "fixture %s is %d bytes" % (name, size))

    def run(*argv):
        out = io.StringIO()
        return okf.main(list(argv), root, out=out, now=NOW), out.getvalue()

    check(okf.IMPL_BUDGET == 35840, "the fixtures assume a 35,840-byte impl cap", okf.IMPL_BUDGET)
    git("init", "--quiet")
    sized(probe, 35700)
    sized("knowledge/impl/roomy.md", 30000, "Roomy")
    write(os.path.join(root, "knowledge", "index.md"), "")
    run("index")
    code, out = run("check", "knowledge/impl")
    jam = [line for line in out.splitlines() if line.startswith("jam list")]
    check(code == 0 and len(jam) == 1 and "knowledge/impl/probe.md 35700/35840" in jam[0],
          "a doc at 35,700 of a 35,840 cap is in the jam list", out)
    check(jam and "roomy.md" not in jam[0], "control: a doc at 30,000 of the same cap isn't", out)
    check(okf.jam_entry(okf.CLAUDE_MD, okf.CLAUDE_MD_BUDGET - 100) is not None
          and okf.jam_entry(okf.CLAUDE_MD, okf.CLAUDE_MD_BUDGET // 2) is None,
          "CLAUDE.md jams against its own 20 KB cap (near it: listed; half full: not)")
    check(okf.jam_entry(".claude/skills/x/SKILL.md", okf.SKILL_BUDGET - 10) is not None
          and okf.jam_entry(".claude/skills/x/SKILL.md", okf.SKILL_BUDGET - 1024) is None,
          "a skill jams against the 25 KB skill cap (10 bytes short: listed; 1 KB short: not)")
    check(okf.jam_line([]).startswith("jam list (within 1% of a cap): none"), "an empty jam list still prints its line")

    staged_list = os.path.join(root, "staged.txt")
    write(staged_list, probe + "\n")
    sized(probe, 35000)
    git("add", "--all")
    git("commit", "--quiet", "-m", "base")
    sized(probe, 35800)
    code, out = run("check", "--staged", staged_list, "knowledge/impl")
    check(code == 1 and "ERROR knowledge/impl/probe.md: [budget] grew from 35000 to 35800" in out,
          "--staged: a commit growing a doc to 35,800 of its 35,840 cap errors", out)
    code, out = run("check", "knowledge/impl")
    check(code == 0 and "grew from" not in out, "control: without --staged (not the pre-commit path) it doesn't", out)
    sized(probe, 35600)
    code, out = run("check", "--staged", staged_list, "knowledge/impl")
    check(code == 0 and "grew from" not in out, "control: growing it to 35,600 (outside the last 0.5%) passes", out)
    sized(probe, 35839)
    git("commit", "--quiet", "-am", "full")
    sized(probe, 35800)
    code, out = run("check", "--staged", staged_list, "knowledge/impl")
    check(code == 0 and "grew from" not in out, "--staged: a commit shrinking a doc to 35,800 passes", out)
    write(staged_list, "knowledge/impl/roomy.md\n")
    sized(probe, 35839)
    git("commit", "--quiet", "-am", "full again")
    sized(probe, 35000)
    git("commit", "--quiet", "-am", "shrunk")
    sized(probe, 35800)
    code, out = run("check", "--staged", staged_list, "knowledge/impl")
    check(code == 0 and "grew from" not in out, "control: a doc grown but not staged in this commit doesn't error", out)
    tree = git("write-tree")
    out = io.StringIO()
    code = okf.main(["budget-tree", tree], root, out=out)
    check(code == 0 and "grew from" not in out.getvalue(),
          "budget-tree (land-pr, pre-merge-commit) never raises the grow error on a doc in the last 0.5%", out.getvalue())
    check(okf.grow_into_cap_errors(probe, 35800, 0) != [], "a new doc born into the last 0.5% counts as grown")
    check(okf.grow_into_cap_errors(probe, 35800, None) == [], "no git HEAD: no grow error")
    # The merge case: a merge of autodev counts a doc only the other side grew as grown by the merge.
    sized(probe, 35800)
    git("commit", "--quiet", "-am", "theirs grew it")
    theirs = git("rev-parse", "HEAD")
    git("reset", "--quiet", "--hard", "HEAD~1")
    write(staged_list, probe + "\n")
    sized(probe, 35800)
    merge_head = os.path.join(root, ".git", "MERGE_HEAD")
    write(merge_head, theirs + "\n")
    code, out = run("check", "--staged", staged_list, "knowledge/impl")
    check(code == 0 and "grew from" not in out, "--staged in a merge: a doc only the other side grew passes", out)
    sized(probe, 35830)
    code, out = run("check", "--staged", staged_list, "knowledge/impl")
    check(code == 1 and "grew from 35800 to 35830" in out, "--staged in a merge: growing past both sides errors", out)
    os.remove(merge_head)
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- prefix families: a.md's topic pages (a-x.md, a-y.md) list as title-only sub-bullets under its entry ---
root = tempfile.mkdtemp(prefix="okf-family-")
try:
    folder = os.path.join(root, "knowledge", "fam")
    write(os.path.join(folder, "a.md"), doc("Implementation", "Alpha", "The overview."))
    write(os.path.join(folder, "a-x.md"), doc("Implementation", "Alpha X", "Topic X, at length."))
    write(os.path.join(folder, "a-y.md"), doc("Implementation", "Alpha Y", "Topic Y, at length."))
    write(os.path.join(folder, "a-y-z.md"), doc("Implementation", "Alpha Y Z", "A topic of a topic."))
    write(os.path.join(folder, "ab.md"), doc("Implementation", "Ab", "Shares a's letters, not its family."))
    write(os.path.join(folder, "b-x.md"), doc("Implementation", "Bee X", "A topic whose overview doesn't exist."))
    text = okf.render_index(folder, os.path.join(root, "knowledge"))
    entries = [line for line in text.split("\n") if line.lstrip().startswith("* ")]
    check(entries == ["* [Ab](ab.md) - Shares a's letters, not its family.",
                      "* [Alpha](a.md) - The overview.",
                      "  * [Alpha X](a-x.md)", "  * [Alpha Y](a-y.md)", "  * [Alpha Y Z](a-y-z.md)",
                      "* [Bee X](b-x.md) - A topic whose overview doesn't exist."],
          "a.md writes one entry with its family (a-x, a-y, and a-y-z one level down) as title-only sub-bullets; "
          "ab.md and b-x.md (no b.md) stay top-level entries", text)
    check(okf.family_parents(["b-x.md", "b-y.md"]) == {}, "control: topic pages with no overview have no family")
    path = os.path.join(folder, "index.md")
    write(path, text)
    check(okf.check_index(path, os.path.join(root, "knowledge")) == [], "a generated index with sub-bullets checks clean",
          okf.check_index(path, os.path.join(root, "knowledge")))
finally:
    shutil.rmtree(root, ignore_errors=True)

# --- growth <base> <tip>: knowledge/ bytes between two refs, without log/ ---
growth_repo = tempfile.mkdtemp(prefix="okf-growth-")
try:
    import subprocess

    def git(*args):
        return subprocess.run(["git", "-C", growth_repo, "-c", "user.name=t", "-c", "user.email=t@t", "-c",
                               "core.autocrlf=false", "-c", "core.hooksPath=/dev/null"] + list(args),
                              capture_output=True, text=True, check=True).stdout.strip()

    git("init", "-q")
    write(os.path.join(growth_repo, "knowledge", "a.md"), "x" * 100)
    git("add", "-A")
    git("commit", "-q", "-m", "base")
    base = git("rev-parse", "HEAD")
    write(os.path.join(growth_repo, "knowledge", "a.md"), "x" * 150)            # +50
    write(os.path.join(growth_repo, "knowledge", "impl", "b.md"), "y" * 200)    # +200
    write(os.path.join(growth_repo, "knowledge", "log", "2026-10.md"), "z" * 7000)  # left out
    write(os.path.join(growth_repo, "README.md"), "z" * 9000)                    # outside the bundle
    git("add", "-A")
    git("commit", "-q", "-m", "tip")
    tip = git("rev-parse", "HEAD")
    out = io.StringIO()
    code = okf.main(["--repo", growth_repo, "growth", base, tip], out=out)
    check(code == 0 and "+250 bytes" in out.getvalue(),
          "growth on a two-commit repo reports the added bytes (log/ and files outside left out)",
          out.getvalue())
    out = io.StringIO()
    code = okf.main(["--repo", growth_repo, "growth", tip, tip], out=out)
    check(code == 0 and "+0 bytes" in out.getvalue(), "growth between identical refs is 0", out.getvalue())
    out = io.StringIO()
    code = okf.main(["--repo", growth_repo, "growth", tip, base], out=out)
    check(code == 0 and "-250 bytes" in out.getvalue(), "growth from the tip back to the base is negative",
          out.getvalue())
    out = io.StringIO()
    code = okf.main(["--repo", growth_repo, "growth", "no-such-ref", tip], out=out)
    check(code == 2 and "can't read no-such-ref" in out.getvalue(), "growth refuses a ref it can't read",
          out.getvalue())
finally:
    shutil.rmtree(growth_repo, ignore_errors=True)

# --- OWNED_PHRASES: a pinned rule stated outside its owner errors ---
owned_repo = tempfile.mkdtemp(prefix="okf-owned-")
try:
    refusals = "6. Land it: `./tools/land-pr.ps1 <n>`. It refuses a base other than `autodev` and a conflicting PR.\n"
    write(os.path.join(owned_repo, "knowledge", "git-workflow.md"), doc() + refusals)
    check(okf.owned_phrase_problems(owned_repo) == [], "each owner may state its own pinned phrase",
          okf.owned_phrase_problems(owned_repo))
    for path, text, what in [
            ("knowledge/impl/other.md", doc() + "Then `land-pr.ps1` refuses an unpushed branch.\n",
             "land-pr's list of refusals"),
            (".claude/skills/autodev/SKILL.md", "5. `./tools/land-pr.ps1 <n>`. It refuses anything but autodev.\n",
             "land-pr's list of refusals"),
            (".claude/agents/child.md", "Then land-pr refuses a base other than autodev.\n",
             "land-pr's list of refusals"),
            ("CLAUDE.md", "`land-pr.ps1` refuses an unpushed branch.\n", "land-pr's list of refusals")]:
        full = os.path.join(owned_repo, path)
        before = open(full, encoding="utf-8").read() if os.path.exists(full) else None
        write(full, text)
        problems = okf.owned_phrase_problems(owned_repo)
        check(len(problems) == 1 and problems[0][0] == path and what in problems[0][1] and "link [" in problems[0][1],
              "a copy of %s in %s errors, naming the owner to link" % (what, path), problems)
        if before is None:
            os.remove(full)
        else:
            write(full, before)
    for path, text, why in [
            ("knowledge/log.md", "# Update log\n\n## 2026-10-28\n* " + refusals, "the log"),
            ("knowledge/impl/merge.md", doc() + "`land-pr.ps1 -SkipBudget` lands without the check.\n",
             "a land-pr mention with no refusal list")]:
        full = os.path.join(owned_repo, path)
        write(full, text)
        check(okf.owned_phrase_problems(owned_repo) == [], "not a copy: " + why, okf.owned_phrase_problems(owned_repo))
        os.remove(full)
    out = io.StringIO()
    write(os.path.join(owned_repo, "knowledge", "impl", "copy.md"), doc() + "land-pr refuses an unpushed branch.\n")
    okf.check_guardrails(owned_repo, out)
    check("ERROR knowledge/impl/copy.md: states land-pr's list of refusals" in out.getvalue(),
          "a whole-bundle check reports the copy as an error", out.getvalue())
finally:
    shutil.rmtree(owned_repo, ignore_errors=True)

# --- the real bundles ---
out = io.StringIO()
code = okf.main(["check"], out=out)
check(code == 0, "knowledge/ passes python tools/okf.py check", out.getvalue())
# The guard on the generated output: the real impl/index.md (once there is one) fits INDEX_BUDGET, so a growing index
# fails this test, not every docs commit. Compact it (prefix families, shorter descriptions); impl/ stays flat.
impl_index = os.path.join(okf.REPO, okf.BUNDLE, "impl", "index.md")
if os.path.exists(impl_index):
    impl_index_size = okf.doc_size(impl_index)
    check(impl_index_size <= okf.INDEX_BUDGET, "the generated knowledge/impl/index.md fits INDEX_BUDGET (%d of %d bytes)"
          % (impl_index_size, okf.INDEX_BUDGET))
check(okf.INDEX_BUDGET <= 30 * okf.KB, "control: INDEX_BUDGET stays a real cap (at most 30 KB), not raised to fit")
check(" 0 error(s)" in " " + out.getvalue().splitlines()[-1], "the real bundle reports no errors",
      out.getvalue()[-2000:])

print("%d failure(s)" % failures)
sys.exit(1 if failures else 0)
