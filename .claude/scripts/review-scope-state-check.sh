#!/usr/bin/env bash
# Verify that a resolver returned the frozen scope the orchestrator supplied.
set -euo pipefail

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "usage: $0 <authoritative.json> <returned.json> [allowed-previous-head]" >&2
  exit 2
fi

python3 - "$1" "$2" "${3:-}" <<'PY'
import json
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
