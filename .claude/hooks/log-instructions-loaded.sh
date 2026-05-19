#!/bin/bash
# InstructionsLoaded hook (opt-in via .claude/settings.local.json):
# log which CLAUDE.md / rules files load, for debugging path-specific rules.
set -euo pipefail

input=$(cat)
log_dir="${CLAUDE_PROJECT_DIR}/.claude/logs"
mkdir -p "$log_dir"
log_file="$log_dir/instructions-loaded.log"

ts=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
path=$(echo "$input" | jq -r '.file_path // "(unknown)"')
kind=$(echo "$input" | jq -r '.memory_type // "(unknown)"')
reason=$(echo "$input" | jq -r '.load_reason // "(unknown)"')
trigger=$(echo "$input" | jq -r '.trigger_file_path // ""')

printf '%s\t%s\t%s\t%s\t%s\n' "$ts" "$kind" "$reason" "$path" "$trigger" >> "$log_file"
exit 0
