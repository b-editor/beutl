#!/usr/bin/env bash
# Verify that a resolver returned the frozen scope the orchestrator supplied.
set -euo pipefail

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "usage: $0 <authoritative.json> <returned.json> [allowed-previous-head]" >&2
  exit 2
fi

python3 - "$1" "$2" "${3:-}" <<'PY'
import json
import re
import sys

authoritative_path, returned_path, allowed_previous = sys.argv[1:]
with open(authoritative_path, encoding="utf-8") as stream:
    authoritative = json.load(stream)
with open(returned_path, encoding="utf-8") as stream:
    returned = json.load(stream)

frozen_fields = (
    "initial_head",
    "intended_behavior",
    "affected_modules",
    "acceptance_tests",
)

def validate(record, label):
    required = (*frozen_fields, "previous_remediation_head")
    missing = [field for field in required if field not in record]
    if missing:
        print(f"{label} review scope is missing: {', '.join(missing)}", file=sys.stderr)
        raise SystemExit(1)

    if not isinstance(record["initial_head"], str) or not re.fullmatch(
        r"[0-9a-f]{40}", record["initial_head"]
    ):
        print(f"{label} review scope has an invalid initial_head", file=sys.stderr)
        raise SystemExit(1)

    previous = record["previous_remediation_head"]
    if previous is not None and (
        not isinstance(previous, str) or not re.fullmatch(r"[0-9a-f]{40}", previous)
    ):
        print(f"{label} review scope has an invalid previous_remediation_head", file=sys.stderr)
        raise SystemExit(1)

    for field in ("intended_behavior", "affected_modules", "acceptance_tests"):
        value = record[field]
        if not isinstance(value, list) or not value or any(
            not isinstance(item, str) or not item.strip() for item in value
        ):
            print(f"{label} review scope has an invalid {field}", file=sys.stderr)
            raise SystemExit(1)

validate(authoritative, "authoritative")
validate(returned, "returned")

for field in frozen_fields:
    if returned.get(field) != authoritative.get(field):
        print(f"review scope changed frozen field: {field}", file=sys.stderr)
        raise SystemExit(1)

expected_previous = authoritative.get("previous_remediation_head")
returned_previous = returned.get("previous_remediation_head")
permitted = {expected_previous}
if allowed_previous:
    permitted.add(allowed_previous)
if returned_previous not in permitted:
    print("review scope advanced to an unverified remediation head", file=sys.stderr)
    raise SystemExit(1)
PY
