"""Checks and indexes knowledge/ as an Open Knowledge Format (OKF) v0.2 bundle (convention:
knowledge/knowledge-format.md).

    python tools/okf.py [--repo DIR] check [--staged FILE|-] [paths]
                                                      conformance + this repo's rules; exit 1 on errors. --staged
                                                      (the pre-commit hook): budget errors only for the listed paths
    python tools/okf.py [--repo DIR] budget-tree <tree>  the size budgets alone, on a git tree (land-pr: the merge)
    python tools/okf.py [--repo DIR] growth <base> <tip>  knowledge/ bytes added from ref base to ref tip, without
                                                      log/ (/promote, after a fetch)
    python tools/okf.py [--repo DIR] index [paths]    regenerate every index.md (or those of the folders given)
    python tools/okf.py [--repo DIR] rotate-log       move knowledge/log.md's oldest days into log/YYYY-MM.md
    python tools/okf.py section <doc.md>#<anchor>     print one heading's section (to the next heading of the same
                                                      or a higher level); exit 1 when the doc or anchor is missing
Paths are repo-relative files or folders inside the bundle (default: the whole bundle). --repo points at another
checkout instead of this one.
Tests: python tools/test-okf.py

What `check` enforces (OKF section 11 plus ours):
  * every concept (.md other than index.md / log.md) opens with a frontmatter block this parser reads;
  * `type` is non-empty and in TYPES; `title` and `description` are present and single-line;
  * `resource` (string or list, repo-relative paths, URLs skipped) exists;
  * `status` is draft | stable | deprecated; `stale_after`, `verified[].at` are ISO 8601 instants with an offset;
    `verified[].by` / `generated.by` follow the actor convention; `stale_after` in the past is a warning;
  * index.md: no frontmatter (bundle root: only `okf_version`), only headings, `* [Title](link) - text` entries (a
    prefix family's topic pages as indented title-only sub-bullets) and HTML comments, and exactly what `index`
    would write (else it prints a diff);
  * log.md: no frontmatter, `## YYYY-MM-DD` headings newest first, bullet entries under them, no bullet twice;
  * log/YYYY-MM.md (the log's monthly archive, written by rotate-log): log.md's format, only that month's dates,
    no size budget, never a concept and no index.md of its own (the folder's index lists each month);
  * size budgets: every file fits DOC_BUDGET (impl/: IMPL_BUDGET), or its BUDGET_ALLOWLIST cap (a generated index.md
    has none and warns above INDEX_BUDGET); with --staged, an unstaged file over budget only warns, and a staged one
    within NEAR_CAP of its budget warns; log.md over its budget says to run rotate-log; the ratchet: an allowlisted
    file that fits its default budget, or whose cap sits over RATCHET_SLACK above allowlist_cap(size), is an error (a
    cap only goes down); an allowlisted file within ALLOWLIST_NEAR bytes of its cap warns "split it" on every check;
    with --staged, a staged doc that grew against HEAD and ends within the last GROW_INTO_CAP of its cap is an error
    (budget-tree never raises it);
  * every check ends with a jam-list line: each checked doc (whole-bundle runs: also skills and CLAUDE.md) within
    JAM_SHARE of its cap, or "none";
  * dated history ("(user, <date>)", "Before <date>", a value history with a date) in a knowledge/ rule doc is an
    error; "until #N" (history by issue number) only warns;
  * whole-bundle runs only: CLAUDE.md fits CLAUDE_MD_BUDGET, and a size line reports the bundle's bytes per folder;
  * whole-bundle runs only: each EXTEND_COPIES file (CLAUDE.md) holds exactly the how-to-extend table between the
    markers in EXTEND_SOURCE (knowledge/patterns.md), which `index` (whole bundle) writes;
  * whole-bundle runs only: an OWNED_PHRASES rule (land-pr's refusals) stated anywhere but its owner file, in
    knowledge/ (not the log), the skills, the agents or CLAUDE.md, is an error;
  * whole-bundle runs only: every top-level folder of MAPPED_CODE_ROOTS (Mod, UI/src, Tests; build output skipped)
    is named in repo-structure.md.

The frontmatter parser handles a YAML subset and fails loudly on anything outside it:
  key: plain scalar | "double quoted" | 'single quoted' | [flow, list] | {flow: map, of: scalars}
  key:                     followed by a block list whose items are scalars, flow maps, or block maps of scalars
    - item                 (- id: x / resource: y continuation lines indented under the dash)
  # full-line comments and blank lines.
Plain scalars may not contain ": " or " #", nor start with an indicator character; quote them. All values are strings.
"""

import datetime
import difflib
import math
import os
import re
import sys
import urllib.parse

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUNDLE = "knowledge"
OKF_VERSION = "0.2"
RESERVED = ("index.md", "log.md")
# The log's monthly archive: <folder>/log/YYYY-MM.md next to <folder>/log.md (knowledge-format.md#where-dated-history-goes).
LOG_ARCHIVE_DIR = "log"
LOG_ARCHIVE_NAME = re.compile(r"^(\d{4}-\d{2})\.md$")
# The project's type vocabulary (knowledge/knowledge-format.md#types). Index sections follow this order.
TYPES = ["Reference", "Workflow", "Convention", "Implementation"]
STATUSES = ("draft", "stable", "deprecated")

# Descriptions of subfolders for the parent's index.md (index.md itself carries no frontmatter to hold one).
# A folder missing here gets a generic line naming how many docs it holds.
FOLDER_DESCRIPTIONS = {
    "impl": "Per-feature implementation docs: what the code does and why, one page per feature.",
}

GENERATED_MARKER = "<!-- Generated by tools/okf.py index from each doc's frontmatter. Don't edit by hand. -->"

KEY = re.compile(r"^([A-Za-z_][A-Za-z0-9_-]*):(?:\s+(.*))?$")
ISO_INSTANT = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$")
ACTOR = re.compile(r"^(human:[\w.-]+|process:[\w.-]+|[\w.-]+/[\w.+-]+)$")
URL = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*://")
ENTRY = re.compile(r"^\* \[([^\]]+)\]\(([^)\s]+)\) - (\S.*)$")
LOG_DATE = re.compile(r"^## (\d{4}-\d{2}-\d{2})\s*$")
PLAIN_START_BAD = set("[]{},#&*!|>'\"%@`")


class FrontmatterError(Exception):
    pass


# ---------------------------------------------------------------- parsing

def parse_scalar(text, where):
    text = text.strip()
    if not text:
        return ""
    if text[0] == '"':
        if len(text) < 2 or not text.endswith('"'):
            raise FrontmatterError("%s: unterminated double-quoted string" % where)
        body = text[1:-1]
        out, i = [], 0
        while i < len(body):
            c = body[i]
            if c == "\\":
                if i + 1 >= len(body):
                    raise FrontmatterError("%s: dangling escape" % where)
                n = body[i + 1]
                out.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\", "/": "/"}.get(n))
                if out[-1] is None:
                    raise FrontmatterError("%s: unsupported escape \\%s" % (where, n))
                i += 2
                continue
            if c == '"':
                raise FrontmatterError("%s: unescaped quote inside a double-quoted string" % where)
            out.append(c)
            i += 1
        return "".join(out)
    if text[0] == "'":
        if len(text) < 2 or not text.endswith("'"):
            raise FrontmatterError("%s: unterminated single-quoted string" % where)
        body = text[1:-1]
        if re.search(r"(?<!')'(?!')", body.replace("''", "")):
            raise FrontmatterError("%s: unescaped quote inside a single-quoted string" % where)
        return body.replace("''", "'")
    if text[0] in PLAIN_START_BAD or text.startswith("- ") or text == "-":
        raise FrontmatterError("%s: a plain value can't start with %r; quote it" % (where, text[0]))
    if ": " in text or text.endswith(":"):
        raise FrontmatterError("%s: a plain value can't contain ': '; quote it" % where)
    if " #" in text:
        raise FrontmatterError("%s: a plain value can't contain ' #' (YAML reads a comment); quote it" % where)
    return text


def split_flow(text, where):
    """Splits the inside of a flow collection on top-level commas, respecting quotes."""
    parts, buf, quote = [], [], None
    for c in text:
        if quote:
            buf.append(c)
            if c == quote:
                quote = None
            continue
        if c in "\"'":
            quote = c
            buf.append(c)
        elif c in "[]{}":
            raise FrontmatterError("%s: nested flow collections aren't supported" % where)
        elif c == ",":
            parts.append("".join(buf))
            buf = []
        else:
            buf.append(c)
    if quote:
        raise FrontmatterError("%s: unterminated quote in a flow collection" % where)
    parts.append("".join(buf))
    if len(parts) == 1 and not parts[0].strip():
        return []
    if any(not p.strip() for p in parts):
        raise FrontmatterError("%s: empty item in a flow collection" % where)
    return parts


def parse_flow_map(text, where):
    result = {}
    for part in split_flow(text[1:-1], where):
        match = KEY.match(part.strip())
        if not match or match.group(2) is None:
            raise FrontmatterError("%s: flow map item %r isn't 'key: value'" % (where, part.strip()))
        result[match.group(1)] = parse_scalar(match.group(2), where)
    return result


def parse_value(text, where):
    text = text.strip()
    if text.startswith("[["):
        raise FrontmatterError("%s: nested flow lists aren't supported" % where)
    if text.startswith("["):
        if not text.endswith("]"):
            raise FrontmatterError("%s: unterminated flow list" % where)
        return [parse_scalar(p, where) for p in split_flow(text[1:-1], where)]
    if text.startswith("{"):
        if not text.endswith("}"):
            raise FrontmatterError("%s: unterminated flow map" % where)
        return parse_flow_map(text, where)
    return parse_scalar(text, where)


def parse_frontmatter(lines, name="frontmatter"):
    """Parses frontmatter lines (between the --- fences) into a dict of strings, lists and dicts."""
    data, i = {}, 0
    while i < len(lines):
        line, where = lines[i], "%s line %d" % (name, i + 2)
        if not line.strip() or line.lstrip().startswith("#"):
            i += 1
            continue
        if line[0] in " \t":
            raise FrontmatterError("%s: unexpected indentation" % where)
        match = KEY.match(line.rstrip())
        if not match:
            raise FrontmatterError("%s: expected 'key: value', got %r" % (where, line))
        key, rest = match.group(1), match.group(2)
        if key in data:
            raise FrontmatterError("%s: duplicate key %r" % (where, key))
        i += 1
        if rest is not None and rest.strip():
            data[key] = parse_value(rest, where)
            continue
        items = []  # block list
        while i < len(lines) and (not lines[i].strip() or lines[i][0] in " \t"):
            item_line = lines[i]
            where = "%s line %d" % (name, i + 2)
            if not item_line.strip() or item_line.strip().startswith("#"):
                i += 1
                continue
            stripped = item_line.strip()
            if not (stripped == "-" or stripped.startswith("- ")):
                raise FrontmatterError("%s: nested maps aren't supported; only block lists ('- item')" % where)
            dash_indent = len(item_line) - len(item_line.lstrip())
            body = stripped[1:].strip()
            i += 1
            first = KEY.match(body) if body and body[0] not in "{['\"" else None
            if first:  # block map item: '- key: value' then indented 'key: value' lines
                entry = {first.group(1): parse_scalar(first.group(2) or "", where)}
                while i < len(lines) and lines[i].strip() and lines[i][0] in " \t" \
                        and not lines[i].strip().startswith("- ") \
                        and len(lines[i]) - len(lines[i].lstrip()) > dash_indent:
                    where = "%s line %d" % (name, i + 2)
                    sub = KEY.match(lines[i].strip())
                    if not sub:
                        raise FrontmatterError("%s: expected 'key: value' in a list item" % where)
                    if sub.group(1) in entry:
                        raise FrontmatterError("%s: duplicate key %r" % (where, sub.group(1)))
                    entry[sub.group(1)] = parse_scalar(sub.group(2) or "", where)
                    i += 1
                items.append(entry)
            elif body.startswith("{"):
                items.append(parse_value(body, where))
            elif body.startswith("["):
                raise FrontmatterError("%s: lists of lists aren't supported" % where)
            else:
                items.append(parse_scalar(body, where))
        data[key] = items if items else ""
    return data


def split_document(text):
    """Returns (frontmatter lines or None, body lines, line number where the body starts)."""
    lines = text.splitlines()
    if not lines or lines[0].rstrip() != "---":
        return None, lines, 1
    for n in range(1, len(lines)):
        if lines[n].rstrip() == "---":
            return lines[1:n], lines[n + 1:], n + 2
    raise FrontmatterError("frontmatter opened with --- but never closed")


def read(path):
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def load(path):
    """(frontmatter dict or None, body lines)."""
    text = read(path)
    if text.startswith("﻿"):
        raise FrontmatterError("file starts with a byte-order mark")
    fm, body, _ = split_document(text)
    return (None if fm is None else parse_frontmatter(fm, os.path.basename(path))), body


def as_list(value):
    if value is None or value == "":
        return []
    return value if isinstance(value, list) else [value]


# ---------------------------------------------------------------- bundle walk

def bundle_root(repo):
    return os.path.join(repo, BUNDLE)


def rel(path, repo):
    return os.path.relpath(path, repo).replace(os.sep, "/")


def is_concept_name(name):
    return name.lower().endswith(".md") and name not in RESERVED


def prune(dirs, keep_log_archive=False):
    """Drops folders that never hold concepts (dot folders, the log archive) from an os.walk in place;
    keep_log_archive keeps the log archive for walks that check files rather than collect concepts."""
    dirs[:] = sorted(d for d in dirs if not d.startswith(".") and (keep_log_archive or d != LOG_ARCHIVE_DIR))


def is_log_archive(path):
    """True for a file inside a log archive folder (<folder>/log/), whatever its name."""
    return os.path.basename(os.path.dirname(os.path.normpath(path))) == LOG_ARCHIVE_DIR


def concepts_in(folder):
    return sorted(n for n in os.listdir(folder) if is_concept_name(n) and os.path.isfile(os.path.join(folder, n)))


def has_concepts(folder):
    if os.path.basename(os.path.normpath(folder)) == LOG_ARCHIVE_DIR:
        return False
    for _, dirs, names in os.walk(folder):
        prune(dirs)
        if any(is_concept_name(n) for n in names):
            return True
    return False


def bundle_folders(root):
    """Every folder in the bundle that holds concepts, directly or below."""
    found = []
    for folder, dirs, _ in os.walk(root):
        prune(dirs)
        if has_concepts(folder):
            found.append(folder)
    return found


def root_of(path, repo):
    """The bundle root (knowledge/) when it holds path, else None."""
    path, root = os.path.normpath(path), os.path.normpath(bundle_root(repo))
    try:
        return root if os.path.commonpath([path, root]) == root else None
    except ValueError:   # another drive on Windows
        return None


# ---------------------------------------------------------------- index generation

def concept_title(fm, name):
    title = fm.get("title") if isinstance(fm, dict) else None
    return title if isinstance(title, str) and title else name[:-3]


def family_parents(names):
    """{name: overview} for each doc in a prefix family: `a-x.md` and `a-y.md` beside `a.md` belong to `a.md`, the
    shortest doc whose stem plus "-" starts theirs (so one level: `a-x-z.md` belongs to `a.md` too). A topic page
    whose overview doesn't exist (`b-x.md`, no `b.md`) has none and stays a top-level entry."""
    stems = sorted({name[:-3] for name in names}, key=lambda stem: (len(stem), stem))
    parents = {}
    for name in names:
        stem = name[:-3]
        for other in stems:
            if len(other) >= len(stem):
                break
            if stem.startswith(other + "-"):
                parents[name] = other + ".md"
                break
    return parents


def render_index(folder, root):
    """The index.md text `index` writes for one folder. A prefix family's topic pages (family_parents) list as
    title-only sub-bullets under their overview's entry, which keeps its description: an index stays a map to the
    overviews, and its size grows by a title per split, not a description."""
    is_root = os.path.normpath(folder) == os.path.normpath(root)
    sections = {}
    names = concepts_in(folder)
    parents = family_parents(names)
    children = {}
    for name in names:
        try:
            fm, _ = load(os.path.join(folder, name))
        except FrontmatterError:
            fm = None
        fm = fm or {}
        if name in parents:
            children.setdefault(parents[name], []).append((concept_title(fm, name), name))
            continue
        kind = fm.get("type") if isinstance(fm.get("type"), str) and fm.get("type") else "Untyped"
        description = fm.get("description") if isinstance(fm.get("description"), str) else ""
        sections.setdefault(kind, []).append((concept_title(fm, name), name, description))
    subfolders = []
    for name in sorted(os.listdir(folder)):
        sub = os.path.join(folder, name)
        if os.path.isdir(sub) and has_concepts(sub):
            key = rel(sub, root)
            count = sum(len(concepts_in(f)) for f in bundle_folders(sub))
            description = FOLDER_DESCRIPTIONS.get(key, "%d doc%s" % (count, "" if count == 1 else "s"))
            subfolders.append((name, name + "/index.md", description))

    out = []
    if is_root:
        out += ["---", 'okf_version: "%s"' % OKF_VERSION, "---", ""]
    out += [GENERATED_MARKER, ""]
    order = [t for t in TYPES if t in sections] + sorted(t for t in sections if t not in TYPES)
    for kind in order:
        out.append("# " + kind)
        out.append("")
        for title, link, description in sorted(sections[kind], key=lambda e: (e[0].lower(), e[1])):
            out.append("* [%s](%s) - %s" % (title, link, description) if description else "* [%s](%s)" % (title, link))
            for child_title, child_link in sorted(children.get(link, []), key=lambda e: (e[0].lower(), e[1])):
                out.append("  * [%s](%s)" % (child_title, child_link))
        out.append("")
    history = []
    if os.path.exists(os.path.join(folder, "log.md")):
        history.append("* [Update log](log.md) - Dated changes to the docs in this folder, newest first.")
    for month in log_archive_months(folder):
        history.append("* [Update log, %s](%s/%s.md) - The log's entries from %s, moved out by rotate-log."
                       % (month, LOG_ARCHIVE_DIR, month, month))
    if history:
        out += ["# History", ""] + history + [""]
    if subfolders:
        out.append("# Folders")
        out.append("")
        for title, link, description in subfolders:
            out.append("* [%s](%s) - %s" % (title, link, description))
        out.append("")
    return "\n".join(out).rstrip("\n") + "\n"


def write_index(folder, root):
    path = os.path.join(folder, "index.md")
    text = render_index(folder, root)
    old = read(path) if os.path.exists(path) else None
    if old == text:
        return False
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    return True


# ---------------------------------------------------------------- checks

def check_instant(value, field, errors):
    if not isinstance(value, str) or not ISO_INSTANT.match(value):
        errors.append("%s must be an ISO 8601 instant with an offset (e.g. 2026-10-03T00:00:00Z), got %r"
                      % (field, value))
        return None
    try:
        return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        errors.append("%s is not a real date: %r" % (field, value))
        return None


def check_actor_events(value, field, errors, require_at):
    events = as_list(value)
    if not events:
        errors.append("%s is empty" % field)
    for event in events:
        if not isinstance(event, dict):
            errors.append("%s entries must be {by, at} maps" % field)
            continue
        by = event.get("by")
        if not isinstance(by, str) or not ACTOR.match(by):
            errors.append("%s.by must be an actor (human:<id>, process:<id> or <producer>/<version>), got %r"
                          % (field, by))
        if "at" in event or require_at:
            check_instant(event.get("at"), field + ".at", errors)


def check_concept(path, repo, now):
    """(errors, warnings) for one concept doc."""
    errors, warnings = [], []
    try:
        fm, _ = load(path)
    except FrontmatterError as error:
        return ["frontmatter: %s" % error], warnings
    if fm is None:
        return ["no frontmatter (every concept doc opens with a --- block carrying at least type, title, "
                "description)"], warnings
    kind = fm.get("type")
    if not isinstance(kind, str) or not kind:
        errors.append("type is missing or empty")
    elif kind not in TYPES:
        errors.append("type %r is not in the vocabulary (%s)" % (kind, ", ".join(TYPES)))
    for key in ("title", "description"):
        value = fm.get(key)
        if not isinstance(value, str) or not value.strip():
            errors.append("%s is missing or empty" % key)
    tags = fm.get("tags")
    if tags is not None and not (isinstance(tags, list) and all(isinstance(t, str) for t in tags)):
        errors.append("tags must be a list of strings")
    for value in as_list(fm.get("resource")):
        if not isinstance(value, str) or not value:
            errors.append("resource entries must be non-empty paths")
        elif URL.match(value):
            continue
        else:
            base = bundle_root(repo) if value.startswith("/") else repo
            if not os.path.exists(os.path.join(base, value.lstrip("/"))):
                errors.append("resource %r doesn't exist (moved or deleted code? update the doc)" % value)
    status = fm.get("status")
    if status is not None and status not in STATUSES:
        errors.append("status must be one of %s, got %r" % (" | ".join(STATUSES), status))
    if "stale_after" in fm:
        when = check_instant(fm["stale_after"], "stale_after", errors)
        if when is not None and now >= when:
            warnings.append("stale since %s: re-check it against the code, then move or drop stale_after"
                            % fm["stale_after"])
    if "verified" in fm:
        check_actor_events(fm["verified"], "verified", errors, require_at=True)
    if "generated" in fm:
        if not isinstance(fm["generated"], dict):
            errors.append("generated must be a {by, at} map")
        else:
            check_actor_events(fm["generated"], "generated", errors, require_at=False)
        warnings.append("generated is not kept in this bundle (git records who and when); remove it")
    for index, source in enumerate(as_list(fm.get("sources"))):
        if not isinstance(source, dict) or not source.get("resource"):
            errors.append("sources[%d] needs a resource" % index)
    return errors, warnings


def check_index(path, root, expected=True):
    errors = []
    try:
        fm, body = load(path)
    except FrontmatterError as error:
        return ["index.md: %s" % error]
    folder = os.path.dirname(path)
    is_root = os.path.normpath(folder) == os.path.normpath(root)
    if fm is not None:
        extra = sorted(set(fm) - ({"okf_version"} if is_root else set()))
        if extra or not is_root:
            errors.append("index.md carries no frontmatter%s (found %s)"
                          % (" except okf_version at the bundle root" if is_root else "", ", ".join(sorted(fm))))
    in_comment = False
    for number, line in enumerate(body, 1):
        stripped = line.strip()
        if in_comment:
            in_comment = "-->" not in stripped
            continue
        if not stripped or re.match(r"^#{1,6} \S", stripped) or ENTRY.match(stripped) \
                or re.match(r"^\* \[[^\]]+\]\([^)\s]+\)$", stripped):
            continue
        if stripped.startswith("<!--"):
            in_comment = "-->" not in stripped
            continue
        errors.append("index.md body line %d isn't a heading or a '* [Title](link) - description' entry: %r"
                      % (number, stripped[:80]))
    if expected:
        want, have = render_index(folder, root), read(path)
        if want != have:
            diff = list(difflib.unified_diff(have.splitlines(), want.splitlines(), "index.md (on disk)",
                                             "index.md (from frontmatter)", n=0, lineterm=""))
            errors.append("out of date with the docs' frontmatter; run: python tools/okf.py index\n"
                          + "\n".join(diff[:24]) + ("\n..." if len(diff) > 24 else ""))
    return errors


def log_archive_months(folder):
    """The months archived in <folder>/log/, newest first."""
    archive = os.path.join(folder, LOG_ARCHIVE_DIR)
    if not os.path.isdir(archive):
        return []
    return sorted((m.group(1) for m in map(LOG_ARCHIVE_NAME.match, os.listdir(archive)) if m), reverse=True)


def check_log(path):
    """Errors for log.md or one of its log/YYYY-MM.md archive files."""
    name = os.path.basename(path)
    month = None
    if is_log_archive(path):
        match = LOG_ARCHIVE_NAME.match(name)
        if not match:
            return ["%s/ holds only YYYY-MM.md archive files (written by python tools/okf.py rotate-log)"
                    % LOG_ARCHIVE_DIR]
        month = match.group(1)
    errors = []
    try:
        fm, body = load(path)
    except FrontmatterError as error:
        return ["%s: %s" % (name, error)]
    if fm is not None:
        errors.append("%s carries no frontmatter" % name)
    dates, current, in_entry, seen = [], None, False, {}
    for number, line in enumerate(body, 1):
        stripped = line.strip()
        if not stripped:
            continue
        match = LOG_DATE.match(line)
        if match:
            try:
                datetime.date.fromisoformat(match.group(1))
            except ValueError:
                errors.append("%s line %d: %s is not a real date" % (name, number, match.group(1)))
            if dates and match.group(1) >= dates[-1]:
                errors.append("%s line %d: %s is not older than %s above it (newest first, one heading per "
                              "day)" % (name, number, match.group(1), dates[-1]))
            if month and not match.group(1).startswith(month + "-"):
                errors.append("%s line %d: %s doesn't belong in the %s archive" % (name, number, match.group(1), month))
            dates.append(match.group(1))
            current, in_entry = match.group(1), False
            continue
        if line.startswith("## "):
            errors.append("%s line %d: date headings are '## YYYY-MM-DD', got %r" % (name, number, stripped))
            continue
        if line.startswith("# ") and current is None:
            continue  # the log's title
        if current is None:
            errors.append("%s line %d: entries go under a '## YYYY-MM-DD' heading" % (name, number))
        elif stripped.startswith(("* ", "- ")):
            in_entry = True
            if stripped in seen:
                errors.append("%s line %d: repeats the entry on line %d (a union merge re-added it?); delete one"
                              % (name, number, seen[stripped]))
            else:
                seen[stripped] = number
        elif in_entry and line[0] in " \t":
            continue  # a wrapped entry
        else:
            errors.append("%s line %d: entries are bullets ('* **Update**: ...'), got %r" % (name, number, stripped[:80]))
    return errors


# ---------------------------------------------------------------- commands

def selected_files(paths, repo):
    root = bundle_root(repo)
    targets = [os.path.join(repo, p) for p in paths] if paths else [root]
    files = []
    for target in targets:
        target = os.path.normpath(target)
        if not os.path.exists(target):
            raise SystemExit("okf: no such path: %s" % rel(target, repo))
        if root_of(target, repo) is None:
            raise SystemExit("okf: %s is outside the bundle (%s)" % (rel(target, repo), BUNDLE))
        if os.path.isfile(target):
            files.append(target)
            continue
        for folder, dirs, names in os.walk(target):
            prune(dirs, keep_log_archive=True)
            files += [os.path.join(folder, n) for n in sorted(names)
                      if n.lower().endswith(".md")]
    return files


# ---------------------------------------------------------------- guardrails: size budgets, folder map

KB = 1024
# Per-file byte budgets (LF line endings). A file over its budget must be split or tightened before it grows.
DOC_BUDGET = 40 * KB      # knowledge/**
IMPL_BUDGET = 35 * KB     # knowledge/impl/**
SKILL_BUDGET = 25 * KB    # .claude/skills/** (checked by tools/check-skills.py)

# Files the user let run over budget. Each cap is allowlist_cap() of the file's size when it was added: +5% headroom
# (BUDGET_HEADROOM), rounded up to the next KB, so in-flight edits fit but the file can't keep growing. A cap only goes down (the ratchet, check_ratchet): `check` fails an entry whose file now fits
# its default budget (delete the entry) or whose cap sits over RATCHET_SLACK above allowlist_cap() of the file's size
# (lower it to the number the error names). Raising a cap is the user's decision, and its entry carries the reason in
# a comment beside it (knowledge-format.md#size-budgets).
BUDGET_HEADROOM = 0.05
RATCHET_SLACK = 1 * KB    # how far a cap may sit above allowlist_cap(size) before check says to lower it
BUDGET_ALLOWLIST = {}

# Every top-level folder of these must be named (as `Folder/`) in the folder map. Build output, dependency and IDE
# folders (MAP_SKIPPED) aren't code.
MAPPED_CODE_ROOTS = ("Mod", "UI/src", "Tests")
MAP_SKIPPED = ("bin", "obj", "node_modules", "build", "Properties", "TestResults", "Library")
FOLDER_MAP = BUNDLE + "/repo-structure.md"


def allowlist_cap(size):
    """The BUDGET_ALLOWLIST cap for an oversized file of `size` bytes: +BUDGET_HEADROOM, rounded up to the next KB."""
    return math.ceil(size * (1 + BUDGET_HEADROOM) / KB) * KB


def budget_for(relpath, allowlist=None):
    """(budget in bytes, allowlisted?) for a repo-relative path."""
    allowlist = BUDGET_ALLOWLIST if allowlist is None else allowlist
    if relpath in allowlist:
        return allowlist[relpath], True
    if relpath.startswith(".claude/skills/"):
        return SKILL_BUDGET, False
    if relpath.startswith(BUNDLE + "/impl/"):
        return IMPL_BUDGET, False
    return DOC_BUDGET, False


def doc_size(path):
    """Size in bytes with LF line endings, so a CRLF checkout measures the same as a Linux one."""
    with open(path, "rb") as handle:
        return len(handle.read().replace(b"\r\n", b"\n"))


# A generated index.md is never refused by a check (it lists every doc in its folder, so a cap would block adding a
# doc); INDEX_BUDGET is a budget on the generator's output instead, enforced by tools/test-okf.py on the real
# knowledge/impl/index.md, and above it a check warns. The fix is a more compact index (prefix families, shorter
# descriptions), not subfolders: impl/ stays flat.
INDEX_BUDGET = 30 * KB  # the generated impl/index.md stays within 30 KB; shorten descriptions, do not raise it
# A staged doc within NEAR_CAP of its budget or cap warns, so a branch trims before a sibling's merge tips it over.
NEAR_CAP = 0.02
# The jam list: every check prints one line naming each doc, skill or CLAUDE.md within JAM_SHARE of its cap.
JAM_SHARE = 0.01
# A staged doc that grew (against HEAD) and ends within the last GROW_INTO_CAP of its cap is an error:
# the commit filled the room a sibling's merge needs. Pre-commit only (check --staged); budget-tree never raises it.
GROW_INTO_CAP = 0.005
# A new impl doc born above this share of its budget warns: split it by topic before it grows.
BORN_WARN = 0.70
# An allowlisted doc within this many bytes of its BUDGET_ALLOWLIST cap warns on every check: split it before the
# next edit has nowhere to go.
ALLOWLIST_NEAR = 512
# The tag on every budget message, so the pre-commit hook can show budget warnings while it hides the others.
BUDGET_TAG = "[budget] "


def budget_problems(relpath, size, allowlist=None):
    """(errors, warnings) for a repo-relative doc of `size` bytes (LF) against its budget. Path rules only, so it
    runs on a working-tree file and on a git blob (budget-tree) alike."""
    if is_log_archive(relpath):
        return [], []  # read by anchor from a link, never loaded to orient; a month is as long as its history
    if os.path.basename(relpath) == "index.md":
        if size > INDEX_BUDGET:
            return [], [BUDGET_TAG + "%d bytes, a generated index over its %d KB budget: compact it (name topic pages "
                        "<overview>-<topic>.md so they list as title-only sub-bullets, shorten long descriptions) and "
                        "run okf.py index; no subfolders, impl/ stays flat"
                        % (size, INDEX_BUDGET // KB)]
        return [], []
    budget, capped = budget_for(relpath, allowlist)
    if capped and check_ratchet(relpath, size, budget):
        return [BUDGET_TAG + e for e in check_ratchet(relpath, size, budget)], []
    if size <= budget:
        if capped and budget - size <= ALLOWLIST_NEAR:
            return [], [BUDGET_TAG + "%d bytes, within %d bytes of its allowlisted cap of %d (tools/okf.py "
                        "BUDGET_ALLOWLIST): split it per topic now; the next edit has no room" % (size, ALLOWLIST_NEAR,
                                                                                                 budget)]
        return [], []
    if os.path.basename(relpath) == "log.md":
        return [BUDGET_TAG + "%d bytes, over the %d KB budget: run python tools/okf.py rotate-log (moves the oldest "
                "days to %s/YYYY-MM.md), then add your entry" % (size, budget // KB, LOG_ARCHIVE_DIR)], []
    if capped:
        return [BUDGET_TAG + "%d bytes, over its allowlisted cap of %d (tools/okf.py BUDGET_ALLOWLIST): this file is "
                "already over budget and may not grow. Split it per topic (the fix that lasts), or tighten or replace "
                "lines instead of adding" % (size, budget)], []
    return [BUDGET_TAG + "%d bytes, over the %d KB budget: split it, or tighten lines instead of adding (incident "
            "history goes to a log, per-system detail to impl/)" % (size, budget // KB)], []


def near_cap_warnings(relpath, size, allowlist=None):
    """A warning for a doc within NEAR_CAP of its budget or cap (none for an index or a log archive)."""
    if is_log_archive(relpath) or os.path.basename(relpath) == "index.md":
        return []
    budget, _ = budget_for(relpath, allowlist)
    if budget * (1 - NEAR_CAP) < size <= budget:
        return [BUDGET_TAG + "%d bytes, within %d%% of its %d-byte budget: a sibling branch's merge can tip it over, "
                "so split or tighten it now" % (size, round(NEAR_CAP * 100), budget)]
    return []


def capped_budget(relpath, allowlist=None):
    """The byte cap jam_entry and grow_into_cap_errors measure against: CLAUDE_MD_BUDGET, or budget_for's; None for an
    index or a log archive (no cap to jam)."""
    if relpath == CLAUDE_MD:
        return CLAUDE_MD_BUDGET
    if is_log_archive(relpath) or os.path.basename(relpath) == "index.md":
        return None
    return budget_for(relpath, allowlist)[0]


def jam_entry(relpath, size, allowlist=None):
    """(relpath, size, cap) when the file sits within JAM_SHARE of its cap (or over it), else None."""
    cap = capped_budget(relpath, allowlist)
    if cap is None or size < cap * (1 - JAM_SHARE):
        return None
    return relpath, size, cap


def jam_line(entries):
    """The one-line jam list every check prints: the files with no room left, fullest first."""
    if not entries:
        return "jam list (within %d%% of a cap): none\n" % round(JAM_SHARE * 100)
    ordered = sorted(entries, key=lambda e: (-e[1] / e[2], e[0]))
    return "jam list (within %d%% of a cap, split them): %s\n" % (
        round(JAM_SHARE * 100), ", ".join("%s %d/%d" % entry for entry in ordered))


def head_size(repo, relpath):
    """The LF size of relpath at HEAD, 0 when HEAD has no such file, or None when there is no git or HEAD.
    In a merge it is the larger of HEAD's and MERGE_HEAD's, so a doc the other side grew isn't counted as grown by the merge."""
    import subprocess
    sizes = []
    for ref in ("HEAD", "MERGE_HEAD"):
        try:
            run = subprocess.run(["git", "-C", repo, "cat-file", "blob", ref + ":" + relpath], capture_output=True)
        except OSError:
            return None
        if run.returncode == 0:
            sizes.append(len(run.stdout.replace(b"\r\n", b"\n")))
    if sizes:
        return max(sizes)
    probe = subprocess.run(["git", "-C", repo, "rev-parse", "--verify", "--quiet", "HEAD"], capture_output=True)
    return 0 if probe.returncode == 0 else None


def grow_into_cap_errors(relpath, size, before, allowlist=None):
    """An error for a staged doc that grew from `before` bytes (HEAD's; 0 for a new doc) to end within the last
    GROW_INTO_CAP of its cap. Over the cap is budget_problems' error instead; a shrink or no HEAD (None) gives none."""
    cap = capped_budget(relpath, allowlist)
    if cap is None or before is None or size <= before or size > cap or size <= cap * (1 - GROW_INTO_CAP):
        return []
    return [BUDGET_TAG + "grew from %d to %d bytes, into the last %.1f%% of its %d-byte cap: a sibling branch's merge "
            "will tip it over, so split it per topic (or tighten lines) in this commit" % (
                before, size, GROW_INTO_CAP * 100, cap)]


def skill_and_claude_md_sizes(repo):
    """[(relpath, LF size)] for CLAUDE.md and every .md under .claude/skills/ (the jam list's non-bundle files)."""
    found = []
    path = os.path.join(repo, CLAUDE_MD)
    if os.path.exists(path):
        found.append((CLAUDE_MD, doc_size(path)))
    skills = os.path.join(repo, ".claude", "skills")
    for dirpath, dirs, names in os.walk(skills):
        dirs.sort()
        for name in sorted(names):
            if name.endswith(".md"):
                full = os.path.join(dirpath, name)
                found.append((rel(full, repo), doc_size(full)))
    return found


def check_budget(path, repo, allowlist=None):
    """Errors for a doc over its byte budget (empty when it fits)."""
    return budget_problems(rel(path, repo), doc_size(path), allowlist)[0]


def budget_tree(tree, repo=REPO, out=None):
    """Budget-checks every knowledge/, skill and agent .md, and CLAUDE.md, in a git tree. land-pr passes the merge of a
    PR head with origin/autodev, so two branches that each fit can't overflow a doc once both land. Exit 1 on errors."""
    import subprocess
    out = out or sys.stdout
    listing = subprocess.run(["git", "-C", repo, "ls-tree", "-r", "-z", "--full-tree", tree], capture_output=True)
    if listing.returncode != 0:
        out.write("okf: git ls-tree %s failed: %s\n" % (tree, listing.stderr.decode("utf-8", "replace").strip()))
        return 2
    blobs = []
    for entry in listing.stdout.decode("utf-8").split("\0"):
        if not entry:
            continue
        meta, path = entry.split("\t", 1)
        kind, oid = meta.split()[1:3]
        covered = path == CLAUDE_MD or (path.endswith(".md") and path.startswith(
            (BUNDLE + "/", ".claude/skills/", ".claude/agents/")))
        if kind == "blob" and covered:
            blobs.append((path, oid))
    batch = subprocess.run(["git", "-C", repo, "cat-file", "--batch"], capture_output=True,
                           input="".join(oid + "\n" for _, oid in blobs).encode("ascii"))
    data, offset, errors, warnings = batch.stdout, 0, 0, 0
    for path, _ in blobs:
        header_end = data.index(b"\n", offset)
        length = int(data[offset:header_end].split()[2])
        size = len(data[header_end + 1:header_end + 1 + length].replace(b"\r\n", b"\n"))
        offset = header_end + 1 + length + 1
        if path == CLAUDE_MD:
            problems = ([BUDGET_TAG + "%d bytes, over its %d KB budget (CLAUDE_MD_BUDGET)" % (size, CLAUDE_MD_BUDGET // KB)]
                        if size > CLAUDE_MD_BUDGET else [])
            notes = []
        else:
            problems, notes = budget_problems(path, size)
        for problem in problems:
            errors += 1
            out.write("ERROR %s: %s\n" % (path, problem))
        for note in notes:
            warnings += 1
            out.write("WARN  %s: %s\n" % (path, note))
    out.write("%d budget error(s), %d warning(s) in %d file(s) of %s\n" % (errors, warnings, len(blobs), tree))
    return 1 if errors else 0


# ---------------------------------------------------------------- guardrails: the budget ratchet, CLAUDE.md, totals

# CLAUDE.md is loaded by every session, so it has its own budget. Checked on whole-bundle runs.
CLAUDE_MD = "CLAUDE.md"
CLAUDE_MD_BUDGET = 18 * KB


def check_ratchet(relpath, size, cap):
    """Errors for an allowlisted file whose cap is looser than its size allows: the file now fits its default budget
    (the entry goes), or the cap sits over RATCHET_SLACK above allowlist_cap(size) (it comes down)."""
    default, _ = budget_for(relpath, {})
    if size <= default:
        return ["%d bytes, which fits the default %d KB budget: delete its BUDGET_ALLOWLIST entry in tools/okf.py "
                "(a cap only goes down)" % (size, default // KB)]
    tight = allowlist_cap(size)
    if cap > tight + RATCHET_SLACK:
        return ["%d bytes, but its BUDGET_ALLOWLIST cap is %d KB: lower it to %d KB in tools/okf.py (a cap only goes "
                "down once its file shrinks)" % (size, cap // KB, tight // KB)]
    return []


def check_claude_md(repo, budget=None):
    """Errors for CLAUDE.md over its budget; none when the checkout has no CLAUDE.md (a test fixture)."""
    budget = CLAUDE_MD_BUDGET if budget is None else budget
    path = os.path.join(repo, CLAUDE_MD)
    if not os.path.exists(path):
        return []
    size = doc_size(path)
    if size > budget:
        return ["%d bytes, over its %d KB budget (tools/okf.py CLAUDE_MD_BUDGET): every session loads it, so move the "
                "detail to the knowledge doc that owns it and leave a link" % (size, budget // KB)]
    return []


MASTER_REF = "origin/master"


def master_sizes(repo):
    """{repo-relative path: bytes} of every .md under knowledge/ on origin/master, or None when the ref (or git) is
    missing, so callers skip the comparison silently."""
    import subprocess
    try:
        run = subprocess.run(["git", "-C", repo, "ls-tree", "-r", "-l", "-z", MASTER_REF, BUNDLE + "/"],
                             capture_output=True)
    except OSError:
        return None
    if run.returncode != 0:
        return None
    sizes = {}
    for entry in run.stdout.decode("utf-8", "replace").split("\0"):
        if "\t" not in entry:
            continue
        meta, path = entry.split("\t", 1)
        fields = meta.split()
        if path.endswith(".md") and len(fields) >= 4 and fields[3].isdigit():
            sizes[path] = int(fields[3])
    return sizes


GROWTH_EXCLUDED = (BUNDLE + "/" + LOG_ARCHIVE_DIR + "/",)


def ref_sizes(repo, ref):
    """{repo-relative path: blob bytes} of every file under knowledge/ at `ref`, without log/ (the log's archive,
    which only ever grows); None when the ref can't be read."""
    import subprocess
    run = subprocess.run(["git", "-C", repo, "ls-tree", "-r", "-l", "-z", ref, "--", BUNDLE + "/"], capture_output=True)
    if run.returncode != 0:
        return None
    sizes = {}
    for entry in run.stdout.decode("utf-8", "replace").split("\0"):
        if "\t" not in entry:
            continue
        meta, path = entry.split("\t", 1)
        fields = meta.split()
        if len(fields) >= 4 and fields[1] == "blob" and fields[3].isdigit() and not path.startswith(GROWTH_EXCLUDED):
            sizes[path] = int(fields[3])
    return sizes


def growth(base, tip, repo=REPO, out=None):
    """One line: knowledge/'s bytes added from ref `base` to ref `tip` (log/ left out). Measures the refs, not the
    working tree, so /promote quotes the promoted tip after a fetch. Exit 2 on a bad ref."""
    out = out or sys.stdout
    sizes = {}
    for ref in (base, tip):
        sizes[ref] = ref_sizes(repo, ref)
        if sizes[ref] is None:
            out.write("okf: can't read %s (fetch first?)\n" % ref)
            return 2
    before, after = sum(sizes[base].values()), sum(sizes[tip].values())
    out.write("growth: %s/ (no log/) %+d bytes (%+.1f KB) from %s to %s: %d KB in %d file(s) at %s\n"
              % (BUNDLE, after - before, (after - before) / KB, base, tip, after // KB, len(sizes[tip]), tip))
    return 0


def born_budget_warnings(relpath, size, master):
    """A warning for a new impl doc (absent from origin/master) born above BORN_WARN of its budget. None `master`
    (no ref) or a doc already on master gives none."""
    if master is None or relpath in master or not relpath.startswith(BUNDLE + "/impl/"):
        return []
    if os.path.basename(relpath) == "index.md" or is_log_archive(relpath):
        return []
    budget, _ = budget_for(relpath)
    if size > BORN_WARN * budget:
        return [BUDGET_TAG + "born at %d%% of its budget: split by topic now" % round(100 * size / budget)]
    return []


def bundle_totals(files, repo, master=None):
    """One report line: the bundle's size in total and per top-level folder, so growth shows without blocking. With
    `master` (master_sizes) it adds the delta since the last promotion."""
    root = os.path.join(repo, BUNDLE)
    folders = {}
    total = count = 0
    for path in files:
        relpath = os.path.relpath(path, root).replace(os.sep, "/")
        if relpath.startswith(".."):
            continue
        size = doc_size(path)
        total += size
        count += 1
        top = relpath.split("/")[0] + "/" if "/" in relpath else "top level"
        folders[top] = folders.get(top, 0) + size
    parts = ", ".join("%s %d KB" % (name, size // KB) for name, size in sorted(folders.items(), key=lambda x: -x[1]))
    delta = ""
    if master is not None:
        delta = " (%+.1f KB since the last promotion)" % ((total - sum(master.values())) / KB)
    return "size: %s/ holds %d KB in %d file(s)%s: %s\n" % (BUNDLE, total // KB, count, delta, parts)


# ---------------------------------------------------------------- guardrails: dated history in rule docs

# Rule docs say what is true now; dated narrative goes to log.md (knowledge-format.md#where-dated-history-goes). A
# line is dated history when it carries a "(user, <date>)" attribution, a "Before/Until/since <date>" phrase, or an
# arrow (a value history: "x 1 → 2") together with a date. Fenced code blocks are skipped.
DATED_HISTORY = (
    re.compile(r"\([Uu]ser\b[^)\n]{0,40}?\b20\d\d-\d\d-\d\d"),
    re.compile(r"\b(?:Before|Until|until|[Ss]ince) 20\d\d-\d\d-\d\d"),
    re.compile(r"\b(?:user|User),? 20\d\d-"),
    re.compile(r"\b[Dd]ecided(?: on)? 20\d\d-"),
    re.compile(r"\b(?:removed|added|changed) on 20\d\d-"),
    # "user's decision, <date>", "(<date>, the user", "playtest <date>", "fixed <date>", and a line that opens with
    # "<date>:" (often a wrapped attribution).
    re.compile(r"\b[Uu]ser'?s? decision,? \(?20\d\d-\d\d-\d\d"),
    re.compile(r"\(20\d\d-\d\d-\d\d,? the user\b"),
    re.compile(r"\b[Pp]laytest,? 20\d\d-\d\d-\d\d"),
    re.compile(r"\b[Ff]ixed (?:on )?20\d\d-\d\d-\d\d"),
    re.compile(r"^\s*(?:[-*+]\s+|\d+\.\s+)?(?:\*\*)?20\d\d-\d\d-\d\d(?:\*\*)?:"),
)
# History by issue number ("Until #418 each state wrote ..."): a warning only, since "until #N lands" is sometimes a
# real dependency rather than the past.
ISSUE_HISTORY = re.compile(r"\b[Uu]ntil #\d+")
DATED_ARROW = "→"
DATE = re.compile(r"\b20\d\d-\d\d-\d\d\b")
# Dated by nature, so not rule docs: the log's archive (log.md itself is skipped by name).
DATED_HISTORY_EXEMPT = (BUNDLE + "/" + LOG_ARCHIVE_DIR + "/",)


def dated_history_lines(text):
    """1-based numbers of the lines outside fenced code that read as dated history."""
    return [number for number, line in prose_lines(text)
            if any(p.search(line) for p in DATED_HISTORY) or (DATED_ARROW in line and DATE.search(line))]


def issue_history_lines(text):
    """1-based numbers of the lines outside fenced code that tell history by issue number ("until #N")."""
    return [number for number, line in prose_lines(text) if ISSUE_HISTORY.search(line)]


def prose_lines(text):
    """(1-based number, line) for each line outside fenced code."""
    fenced = False
    for number, line in enumerate(text.split("\n"), 1):
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        if not fenced:
            yield number, line


def shown_lines(lines):
    return ", ".join(str(n) for n in lines[:8]) + (", ..." if len(lines) > 8 else "")


def check_dated_history(path, repo):
    """(errors, warnings) for dated history in a knowledge/ rule doc: an error; history by issue number warns."""
    relpath = rel(path, repo)
    if not relpath.startswith(BUNDLE + "/") or relpath.startswith(DATED_HISTORY_EXEMPT):
        return [], []
    if os.path.basename(path) in RESERVED:
        return [], []
    text = read(path)
    lines = dated_history_lines(text)
    by_issue = issue_history_lines(text)
    issue_notes = ["%d line(s) of history by issue number (\"until #N\", line %s): say what is true now, unless the "
                   "line names a real dependency (\"until #N lands\")" % (len(by_issue), shown_lines(by_issue))
                   ] if by_issue else []
    if not lines:
        return [], issue_notes
    return ["%d line(s) of dated history (line %s): a rule doc says what is true now. Keep the current value; move "
            "the history to log.md" % (len(lines), shown_lines(lines))], issue_notes


def check_folder_map(repo):
    """Errors for top-level code folders the folder map (knowledge/repo-structure.md) doesn't name."""
    map_path = os.path.join(repo, FOLDER_MAP)
    roots = [r for r in MAPPED_CODE_ROOTS if os.path.isdir(os.path.join(repo, r))]
    if not roots:
        return []
    if not os.path.exists(map_path):
        return ["%s is missing; it maps %s" % (FOLDER_MAP, ", ".join(roots))]
    text = read(map_path)
    errors = []
    for root in roots:
        for name in sorted(os.listdir(os.path.join(repo, root))):
            if not os.path.isdir(os.path.join(repo, root, name)) or name.startswith(".") or name in MAP_SKIPPED:
                continue
            if "`%s/" % name not in text:
                errors.append("%s/%s/ isn't named in %s; add a row or a mention (`%s/`)"
                              % (root, name, FOLDER_MAP, name))
    return errors


# ---------------------------------------------------------------- the how-to-extend index and its generated copies

# One source for "how to extend": the table between the markers in EXTEND_SOURCE. `index` writes it into each
# EXTEND_COPIES file between the same markers (links rebased to the copy's folder); `check` fails a copy that differs.
EXTEND_SOURCE = "knowledge/patterns.md"
EXTEND_COPIES = ("CLAUDE.md",)
EXTEND_START = "<!-- how-to-extend:start"
EXTEND_END = "<!-- how-to-extend:end -->"
EXTEND_COPY_START = "<!-- how-to-extend:start (generated by tools/okf.py index from %s; edit it there) -->" % EXTEND_SOURCE


def extend_block(text):
    """(lines, start, end): the text's lines and the indexes of its one start and one end marker line, or None."""
    lines = text.split("\n")
    starts = [i for i, line in enumerate(lines) if line.strip().startswith(EXTEND_START)]
    ends = [i for i, line in enumerate(lines) if line.strip() == EXTEND_END]
    if len(starts) != 1 or len(ends) != 1 or ends[0] < starts[0]:
        return None
    return lines, starts[0], ends[0]


def render_extend_copy(repo, copy):
    """(the text `copy` should have, None), (None, problem), or (None, None) in a checkout with no source (a test
    fixture: nothing to copy)."""
    source_path, copy_path = os.path.join(repo, EXTEND_SOURCE), os.path.join(repo, copy)
    if not os.path.exists(source_path):
        return None, None
    source = extend_block(read(source_path))
    if source is None:
        return None, "%s needs exactly one '%s ... -->' line and one '%s' line after it" \
            % (EXTEND_SOURCE, EXTEND_START, EXTEND_END)
    lines, start, end = source
    own = os.path.basename(source_path)
    table = [rebase_links(line.replace("](#", "](%s#" % own), os.path.dirname(source_path),
                          os.path.dirname(copy_path)) for line in lines[start + 1:end]]
    target = extend_block(read(copy_path)) if os.path.exists(copy_path) else None
    if target is None:
        return None, "needs the generated how-to-extend block: a '%s ... -->' line and a '%s' line" \
            % (EXTEND_START, EXTEND_END)
    copy_lines, copy_start, copy_end = target
    return "\n".join(copy_lines[:copy_start] + [EXTEND_COPY_START] + table + copy_lines[copy_end:]), None


def write_extend_copies(repo):
    """Writes every stale copy (keeping its line endings); returns the repo-relative paths written."""
    written = []
    for copy in EXTEND_COPIES:
        want, problem = render_extend_copy(repo, copy)
        if problem:
            raise SystemExit("okf: %s: %s" % (copy, problem))
        if want is None:
            continue
        path = os.path.join(repo, copy)
        if read(path) == want:
            continue
        with open(path, "rb") as handle:
            newline = "\r\n" if b"\r\n" in handle.read() else "\n"
        with open(path, "w", encoding="utf-8", newline=newline) as handle:
            handle.write(want)
        written.append(copy)
    return written


def check_extend_copies(repo):
    """(path, problem) for each how-to-extend copy that is missing its block or differs from the source."""
    problems = []
    for copy in EXTEND_COPIES:
        want, problem = render_extend_copy(repo, copy)
        if want is not None and read(os.path.join(repo, copy)) != want:
            problem = ("its how-to-extend table differs from %s's (edit the source, not the copy); run: "
                       "python tools/okf.py index" % EXTEND_SOURCE)
        if problem:
            problems.append((copy, problem))
    return problems


# ---------------------------------------------------------------- guardrails: one owner per copied step

# A rule copied into several files drifts one edit at a time, and a reader can't tell which copy is current. Each entry pins a
# rule's wording to the one file that states it: (pattern, owner, what it is, the owner's anchor to link instead). A
# whole-bundle `check` errors on a pinned phrase anywhere else in knowledge/ (not the log), the skills,
# the agents or CLAUDE.md. Add an entry when a step's copies have drifted once; collapse the copies to links first.
OWNED_PHRASES = (
    (re.compile(r"\bland-pr(?:\.ps1)?\b[^\n]{0,120}?\brefuses\b[^\n]{0,60}?\b(?:base|unpushed|conflict|budget|anything but)"),
     BUNDLE + "/git-workflow.md", "land-pr's list of refusals", "autonomous-sessions-autodev"),
)
OWNED_PHRASE_EXEMPT = (BUNDLE + "/" + LOG_ARCHIVE_DIR + "/", BUNDLE + "/log.md")


def owned_phrase_files(repo):
    """Repo-relative paths of the .md files the owned-phrase check reads: knowledge/ (less OWNED_PHRASE_EXEMPT), the
    skills, the agents and CLAUDE.md."""
    found = [CLAUDE_MD] if os.path.exists(os.path.join(repo, CLAUDE_MD)) else []
    for top in (BUNDLE, os.path.join(".claude", "skills"), os.path.join(".claude", "agents")):
        for dirpath, dirs, names in os.walk(os.path.join(repo, top)):
            dirs.sort()
            for name in sorted(names):
                relpath = rel(os.path.join(dirpath, name), repo)
                if name.endswith(".md") and not relpath.startswith(OWNED_PHRASE_EXEMPT):
                    found.append(relpath)
    return found


def owned_phrase_problems(repo, phrases=None):
    """(path, problem) for each pinned phrase (OWNED_PHRASES) stated outside its owner."""
    phrases = OWNED_PHRASES if phrases is None else phrases
    problems = []
    for relpath in owned_phrase_files(repo):
        lines = read(os.path.join(repo, relpath)).split("\n")
        for pattern, owner, what, anchor in phrases:
            if relpath == owner:
                continue
            hits = [n for n, line in enumerate(lines, 1) if pattern.search(line)]
            if hits:
                problems.append((relpath, "states %s (line %s), which %s owns: link [%s](%s#%s) instead of copying it "
                                          "(OWNED_PHRASES in tools/okf.py)"
                                 % (what, shown_lines(hits), owner, os.path.basename(owner), owner, anchor)))
    return problems


def staged_budget_split(relpath, problems, staged):
    """(errors, warnings) for a file's budget errors: with a staged set (the pre-commit hook), a file the commit
    doesn't stage only warns, so an overflow another branch's merge left can't block an unrelated commit."""
    if staged is None or relpath in staged:
        return problems, []
    return [], [BUDGET_TAG + "not staged in this commit, so it doesn't block it: " + p.replace(BUDGET_TAG, "", 1)
                for p in problems]


def check_guardrails(repo, out, staged=None):
    """The whole-bundle guardrails (how-to-extend copies, folder map, owned phrases, CLAUDE.md). Returns
    (errors, warnings)."""
    errors = warnings = 0
    for path, problem in check_extend_copies(repo):
        errors += 1
        out.write("ERROR %s: %s\n" % (path, problem))
    for problem in check_folder_map(repo):
        errors += 1
        out.write("ERROR %s: %s\n" % (FOLDER_MAP, problem))
    for path, problem in owned_phrase_problems(repo):
        errors += 1
        out.write("ERROR %s: %s\n" % (path, problem))
    claude_errors, claude_notes = staged_budget_split(
        CLAUDE_MD, [BUDGET_TAG + p for p in check_claude_md(repo)], staged)
    for problem in claude_errors:
        errors += 1
        out.write("ERROR %s: %s\n" % (CLAUDE_MD, problem))
    for note in claude_notes:
        warnings += 1
        out.write("WARN  %s: %s\n" % (CLAUDE_MD, note))
    return errors, warnings


def check(paths, repo=REPO, now=None, out=None, staged=None):
    """Runs the checks; returns the exit code. `staged` (a set of repo-relative paths, from --staged) limits budget
    errors to those files (the rest warn) and adds the near-cap warning for them; every other error stays whole-tree."""
    out = out or sys.stdout
    now = now or datetime.datetime.now(datetime.timezone.utc)
    files = selected_files(paths, repo)
    errors = warnings = 0
    folders = set()
    jammed = []
    master = master_sizes(repo)
    for path in files:
        folders.add(os.path.dirname(path))
        name = os.path.basename(path)
        if name == "index.md":
            problems, notes = check_index(path, root_of(path, repo)), []
        elif name == "log.md" or is_log_archive(path):
            problems, notes = check_log(path), []
        else:
            problems, notes = check_concept(path, repo, now)
        dated_errors, dated_notes = check_dated_history(path, repo)
        relpath, size = rel(path, repo), doc_size(path)
        budget_errors, budget_notes = budget_problems(relpath, size)
        budget_errors, unstaged_notes = staged_budget_split(relpath, budget_errors, staged)
        if staged is not None and relpath in staged:
            budget_notes = budget_notes + near_cap_warnings(relpath, size)
            if capped_budget(relpath) is not None and size > capped_budget(relpath) * (1 - GROW_INTO_CAP):
                budget_errors = budget_errors + grow_into_cap_errors(relpath, size, head_size(repo, relpath))
        jam = jam_entry(relpath, size)
        if jam:
            jammed.append(jam)
        problems = problems + budget_errors + dated_errors
        notes = notes + dated_notes + budget_notes + unstaged_notes + born_budget_warnings(relpath, size, master)
        for problem in problems:
            errors += 1
            out.write("ERROR %s: %s\n" % (rel(path, repo), problem))
        for note in notes:
            warnings += 1
            out.write("WARN  %s: %s\n" % (rel(path, repo), note))
    for folder in sorted(folders):
        if has_concepts(folder) and not os.path.exists(os.path.join(folder, "index.md")):
            errors += 1
            out.write("ERROR %s: no index.md; run: python tools/okf.py index %s\n"
                      % (rel(folder, repo), rel(root_of(folder, repo), repo)))
    if not paths:
        more_errors, more_warnings = check_guardrails(repo, out, staged)
        errors, warnings = errors + more_errors, warnings + more_warnings
        out.write(bundle_totals(files, repo, master))
        jammed += [e for e in (jam_entry(p, size) for p, size in skill_and_claude_md_sizes(repo)) if e]
    out.write(jam_line(jammed))
    out.write("%d error(s), %d warning(s) in %d file(s) checked\n" % (errors, warnings, len(files)))
    return 1 if errors else 0


def index(paths, repo=REPO, out=None):
    out = out or sys.stdout
    if paths:
        folders = []
        for p in paths:
            full = os.path.normpath(os.path.join(repo, p))
            if not os.path.isdir(full):
                raise SystemExit("okf: index takes folders, not %s" % p)
            if root_of(full, repo) is None:
                raise SystemExit("okf: %s is outside the bundle (%s)" % (p, BUNDLE))
            folders += bundle_folders(full)
    else:
        folders = bundle_folders(bundle_root(repo))
    changed = 0
    for folder in folders:
        if write_index(folder, root_of(folder, repo)):
            changed += 1
            out.write("wrote %s\n" % rel(os.path.join(folder, "index.md"), repo))
    if not paths:
        for copy in write_extend_copies(repo):
            out.write("wrote the how-to-extend table in %s\n" % copy)
    out.write("%d index file(s) updated, %d checked\n" % (changed, len(folders)))
    return 0


# ---------------------------------------------------------------- rotate-log

LOG_ROTATE_TARGET = DOC_BUDGET // 2   # rotate-log leaves log.md at most this big (today's entries aside)
LOG_LINK = re.compile(r"\]\(([^()\s#]*log\.md)#(\d{4}-\d{2}-\d{2})\)")
LOG_LINK_ROOTS = (BUNDLE, ".claude/skills", "CLAUDE.md")   # where links into the log's day anchors live


MD_LINK = re.compile(r"(\]\()([^()\s]+)(\))")
LINK_SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*:")


def rebase_links(line, src_dir, dst_dir):
    """A log line moved from src_dir to dst_dir, its relative markdown links rewritten to point at the same files."""
    def fix(match):
        target = match.group(2)
        if LINK_SCHEME.match(target) or target.startswith(("/", "#")):
            return match.group(0)
        path, sep, anchor = target.partition("#")
        new = os.path.relpath(os.path.normpath(os.path.join(src_dir, path)), dst_dir).replace(os.sep, "/")
        return match.group(1) + new + sep + anchor + match.group(3)
    return MD_LINK.sub(fix, line)


def trim_blank(lines):
    while lines and not lines[-1].strip():
        lines = lines[:-1]
    return lines


def split_log(text):
    """(title lines, [(date, lines)]) for a log's text; each day's lines start with its heading, blank lines trimmed."""
    title, days = [], []
    for line in text.splitlines():
        match = LOG_DATE.match(line)
        if match:
            days.append((match.group(1), [line]))
        elif days:
            days[-1][1].append(line)
        else:
            title.append(line)
    return trim_blank(title), [(date, trim_blank(lines)) for date, lines in days]


def join_log(title, days):
    blocks = ["\n".join(title)] if title else []
    blocks += ["\n".join(lines) for _, lines in days]
    return "\n\n".join(blocks) + "\n"


def write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def rotate_log(repo=REPO, now=None, target=LOG_ROTATE_TARGET, out=None):
    """Moves log.md's oldest whole days into log/YYYY-MM.md until log.md fits target, never today's (siblings are
    still adding to it) nor the newest day's. "Today" is the local date: entries are dated in local time, so a UTC date
    already past it would move today's entries and empty log.md; the newest-day clamp covers a clock or zone
    ahead of whoever wrote the entries. Merges into an existing month (a day already there takes the moved bullets on top) and
    repoints log.md#YYYY-MM-DD links at the archive. Returns the moved dates, newest first."""
    out = out or sys.stdout
    now = now or datetime.datetime.now()   # local time, the zone the log's day headings are written in
    folder = bundle_root(repo)
    path = os.path.join(folder, "log.md")
    title, days = split_log(read(path))
    today = now.date().isoformat()
    if days:
        today = min(today, max(date for date, _ in days))
    moved = []
    while days and days[-1][0] < today and len(join_log(title, days).encode("utf-8")) > target:
        moved.append(days.pop())
    if not moved:
        out.write("log.md is %d bytes (target %d): nothing to move\n" % (doc_size(path), target))
        return []
    archive_dir = os.path.join(folder, LOG_ARCHIVE_DIR)
    by_month = {}
    for date, lines in moved:
        lines = [rebase_links(line, folder, archive_dir) for line in lines]
        by_month.setdefault(date[:7], []).append((date, lines))
    for month, month_days in sorted(by_month.items()):
        archive = os.path.join(archive_dir, month + ".md")
        if os.path.exists(archive):
            arch_title, existing = split_log(read(archive))
        else:
            arch_title, existing = ["# Update log, " + month], []
        merged = dict(existing)
        for date, lines in month_days:
            merged[date] = lines + merged[date][1:] if date in merged else lines
        write_text(archive, join_log(arch_title, sorted(merged.items(), reverse=True)))
        out.write("moved %d day(s) to %s\n" % (len(month_days), rel(archive, repo)))
    write_text(path, join_log(title, days))
    moved_dates = set(date for date, _ in moved)
    for changed in repoint_log_links(repo, path, moved_dates):
        out.write("repointed log links in %s\n" % changed)
    return sorted(moved_dates, reverse=True)


def repoint_log_links(repo, log_path, dates):
    """Rewrites links to log_path#<date> for the given dates to log/<month>.md#<date>; returns the files changed."""
    files = []
    for root in LOG_LINK_ROOTS:
        full = os.path.join(repo, root)
        if os.path.isfile(full):
            files.append(full)
        elif os.path.isdir(full):
            for folder, dirs, names in os.walk(full):
                prune(dirs, keep_log_archive=True)
                files += [os.path.join(folder, n) for n in sorted(names) if n.lower().endswith(".md")]
    changed = []
    archive_dir = os.path.join(os.path.dirname(log_path), LOG_ARCHIVE_DIR)
    for path in files:
        text = read(path)
        base = os.path.dirname(path)

        def repoint(match):
            link, date = match.group(1), match.group(2)
            if date not in dates or os.path.normpath(os.path.join(base, urllib.parse.unquote(link))) \
                    != os.path.normpath(log_path):
                return match.group(0)
            new = os.path.relpath(os.path.join(archive_dir, date[:7] + ".md"), base).replace(os.sep, "/")
            return "](%s#%s)" % (new, date)

        new_text = LOG_LINK.sub(repoint, text)
        if new_text != text:
            write_text(path, new_text)
            changed.append(rel(path, repo))
    return changed


# ---------------------------------------------------------------- section

_doc_links = None


def doc_links():
    """tools/check-doc-links.py, whose GitHub heading slugs are the one definition of an anchor."""
    global _doc_links
    if _doc_links is None:
        import importlib.util
        spec = importlib.util.spec_from_file_location("check_doc_links", os.path.join(REPO, "tools",
                                                                                      "check-doc-links.py"))
        _doc_links = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_doc_links)
    return _doc_links


def section_lines(text, anchor):
    """The lines of the section whose heading slug is anchor (GitHub de-duplication: "-1", "-2" for repeats), from
    its heading to the next heading of the same or a higher level, trailing blanks trimmed; None when no heading
    has that anchor. Headings inside fenced code don't count."""
    links = doc_links()
    lines = text.splitlines()
    counts, fence, start, level = {}, None, None, None
    for number, line in enumerate(lines):
        match = links.FENCE.match(line)
        if match:
            if fence is None:
                fence = match.group(1)
            elif match.group(1) == fence:
                fence = None
            continue
        heading = links.HEADING.match(line) if fence is None else None
        if not heading:
            continue
        depth = len(heading.group(1))
        if start is not None:
            if depth <= level:
                return trim_blank(lines[start:number])
            continue
        slug = links.slugify(heading.group(2))
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        if (slug if count == 0 else "%s-%d" % (slug, count)) == anchor.lower():
            start, level = number, depth
    return None if start is None else trim_blank(lines[start:])


def section(target, repo=REPO, out=None):
    """Prints <doc.md>#<anchor>'s section; the path is taken as given, else against the repo root. Exit 1 when the
    doc or the anchor is missing."""
    out = out or sys.stdout
    path, _, anchor = target.partition("#")
    anchor = urllib.parse.unquote(anchor)
    if not path or not anchor:
        sys.stderr.write("okf: section takes <doc.md>#<anchor>\n")
        return 2
    path = urllib.parse.unquote(path)
    if not os.path.isfile(path) and os.path.isfile(os.path.join(repo, path)):
        path = os.path.join(repo, path)
    if not os.path.isfile(path):
        out.write("okf: no such doc: %s\n" % path)
        return 1
    lines = section_lines(read(path), anchor)
    if lines is None:
        out.write("okf: %s has no heading with anchor #%s\n" % (path, anchor))
        return 1
    out.write("\n".join(lines) + "\n")
    return 0


def main(argv, repo=REPO, out=None, now=None):
    argv = list(argv)
    if len(argv) >= 2 and argv[0] == "--repo":
        repo = os.path.abspath(argv[1])
        argv = argv[2:]
    if not argv or argv[0] not in ("check", "index", "rotate-log", "section", "budget-tree", "growth"):
        sys.stderr.write(__doc__)
        return 2
    if argv[0] == "check":
        args, staged = argv[1:], None
        if args[:1] == ["--staged"]:
            if len(args) < 2:
                raise SystemExit("okf: --staged takes a file of paths, one per line (- for stdin)")
            handle = sys.stdin if args[1] == "-" else open(args[1], encoding="utf-8")
            staged = {line.strip().replace("\\", "/") for line in handle if line.strip()}
            args = args[2:]
        return check(args, repo, now=now, out=out, staged=staged)
    if argv[0] == "budget-tree":
        if len(argv) != 2:
            raise SystemExit("okf: budget-tree takes one tree-ish")
        return budget_tree(argv[1], repo, out=out)
    if argv[0] == "growth":
        if len(argv) != 3:
            raise SystemExit("okf: growth takes two refs: <base> <tip>")
        return growth(argv[1], argv[2], repo, out=out)
    if argv[0] == "section":
        if len(argv) != 2:
            raise SystemExit("okf: section takes one <doc.md>#<anchor>")
        return section(argv[1], repo, out=out)
    if argv[0] == "rotate-log":
        if len(argv) > 1:
            raise SystemExit("okf: rotate-log takes no arguments")
        rotate_log(repo, now=now, out=out)
        return 0
    return index(argv[1:], repo, out=out)


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main(sys.argv[1:]))
