#!/usr/bin/env python3
"""Changed-line coverage probe for /beutl-loop's auto-merge gate (B-4).

Computes the percentage of *added* lines in the diff that are covered by
tests, using a cobertura XML coverage report.  Exit 0 + print the percentage
if >= threshold (default 70); exit 1 + print the percentage if < threshold.

Usage:
  changed-line-coverage.py <base_ref> <head_ref> <cobertura_xml> [--threshold N]

If the cobertura XML is missing or unparseable, exit 2 (probe failed — the
caller treats this as high-risk / fail-safe).
"""
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# @@ -old_start[,old_count] +new_start[,new_count] @@
HUNK_RE = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)")


def added_lines_per_file(base: str, head: str) -> dict[str, list[int]]:
    """Return {filepath: [line numbers added in the diff]}, scoped to src/ only.

    The coverage gate is about production code under-test; test files, docs,
    and .claude/ changes have no corresponding Cobertura entries and would
    inflate the denominator with guaranteed-uncovered lines.
    """
    diff = subprocess.check_output(
        ["git", "diff", f"{base}...{head}", "--unified=0", "--diff-filter=d", "--", "src/"],
        text=True,
    )
    result: dict[str, list[int]] = {}
    current_file = None
    new_line = 0
    for line in diff.splitlines():
        if line.startswith("+++ b/"):
            current_file = line[6:]
            result.setdefault(current_file, [])
        elif line.startswith("@@ -"):
            m = HUNK_RE.match(line)
            if m:
                new_line = int(m.group(1))
        elif line.startswith("+") and current_file is not None:
            result[current_file].append(new_line)
            new_line += 1
        elif line.startswith(" ") and current_file is not None:
            new_line += 1
    return {f: lines for f, lines in result.items() if lines}


def parse_cobertura(xml_path: str) -> dict[str, dict[int, bool]]:
    """Return {filepath: {line_number: covered}} from a cobertura XML."""
    tree = ET.parse(xml_path)
    root = tree.getroot()
    result: dict[str, dict[int, bool]] = {}
    for cls in root.iter("class"):
        filename = cls.get("filename")
        if not filename:
            continue
        # Merge entries for the same file: Coverlet emits one <class> per type,
        # so partial or multiple classes in a single .cs produce several entries.
        # A line counts as covered if ANY entry reports a hit.
        lines = result.setdefault(filename, {})
        for ln in cls.iter("line"):
            num = int(ln.get("number", 0))
            hits = int(ln.get("hits", 0))
            lines[num] = lines.get(num, False) or hits > 0
    return result


def match_file(diff_file: str, cobertura_files: dict[str, dict[int, bool]]) -> str | None:
    """Match a diff path to a cobertura filename (suffix match)."""
    if diff_file in cobertura_files:
        return diff_file
    # Cobertura often stores paths relative to the test project, so do a
    # suffix match on the path components.
    diff_parts = Path(diff_file).parts
    for cb_file in cobertura_files:
        cb_parts = Path(cb_file).parts
        # Compare the common-length suffix in both directions: Coverlet may store
        # a shorter project-relative path (e.g. "Foo.cs") than the src/-rooted
        # diff path, or vice versa.
        n = min(len(cb_parts), len(diff_parts))
        if n and cb_parts[-n:] == diff_parts[-n:]:
            return cb_file
    return None


def main() -> int:
    args = sys.argv[1:]
    threshold = 70
    if "--threshold" in args:
        idx = args.index("--threshold")
        threshold = int(args[idx + 1])
        args = args[:idx] + args[idx + 2:]
    if len(args) < 3:
        print("usage: changed-line-coverage.py <base> <head> <cobertura.xml> [--threshold N]", file=sys.stderr)
        return 2

    base, head, xml_path = args[0], args[1], args[2]

    if not Path(xml_path).is_file():
        print(f"coverage probe failed: {xml_path} not found", file=sys.stderr)
        return 2

    try:
        cobertura = parse_cobertura(xml_path)
    except Exception as e:
        print(f"coverage probe failed: cannot parse {xml_path}: {e}", file=sys.stderr)
        return 2

    added = added_lines_per_file(base, head)
    if not added:
        print("changed-line coverage: 100.0% (no added lines in src/)")
        return 0

    total_added = 0
    total_covered = 0
    unmatched: list[str] = []

    for diff_file, line_nums in added.items():
        cb_key = match_file(diff_file, cobertura)
        if cb_key is None:
            unmatched.append(diff_file)
            total_added += len(line_nums)
            continue
        cb_lines = cobertura[cb_key]
        for ln in line_nums:
            # Count only lines Coverlet instrumented. Non-sequence-point lines
            # (using directives, braces, declarations, attributes, comments) are
            # absent from the map and must not inflate the denominator.
            if ln not in cb_lines:
                continue
            total_added += 1
            if cb_lines[ln]:
                total_covered += 1

    if total_added == 0:
        print("changed-line coverage: 100.0% (no added lines)")
        return 0

    pct = 100.0 * total_covered / total_added

    if unmatched:
        print(f"changed-line coverage: {pct:.1f}% ({total_covered}/{total_added} added lines covered)", file=sys.stderr)
        print(f"  unmatched files (counted as uncovered): {', '.join(unmatched)}", file=sys.stderr)
    else:
        print(f"changed-line coverage: {pct:.1f}% ({total_covered}/{total_added} added lines covered)")

    if pct >= threshold:
        return 0
    return 1


if __name__ == "__main__":
    sys.exit(main())
