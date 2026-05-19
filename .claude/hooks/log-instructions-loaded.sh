#!/bin/bash
# InstructionsLoaded hook (opt-in via .claude/settings.local.json):
# log which CLAUDE.md / rules files load, for debugging path-specific rules.
#
# Best-effort: this hook is diagnostic only, so every failure path falls
# through to `exit 0` instead of leaking a non-zero status (which would
# look like a deny to the harness).
set -uo pipefail

input=$(cat || true)

# `CLAUDE_PROJECT_DIR` is normally set by the harness; fall back to the
# current working directory so an unexpected environment cannot turn the
# diagnostic hook into a startup failure.
project_dir="${CLAUDE_PROJECT_DIR:-$PWD}"
log_dir="$project_dir/.claude/logs"
log_file="$log_dir/instructions-loaded.log"

mkdir -p "$log_dir" 2>/dev/null || exit 0
[ -w "$log_dir" ] || exit 0

ts=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
path=$(printf '%s' "$input" | jq -r '.file_path // "(unknown)"' 2>/dev/null || echo "(unknown)")
kind=$(printf '%s' "$input" | jq -r '.memory_type // "(unknown)"' 2>/dev/null || echo "(unknown)")
reason=$(printf '%s' "$input" | jq -r '.load_reason // "(unknown)"' 2>/dev/null || echo "(unknown)")
trigger=$(printf '%s' "$input" | jq -r '.trigger_file_path // ""' 2>/dev/null || echo "")

printf '%s\t%s\t%s\t%s\t%s\n' "$ts" "$kind" "$reason" "$path" "$trigger" >> "$log_file" 2>/dev/null || true
exit 0
