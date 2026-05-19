#!/bin/bash
# Project statusline: branch, ahead/behind origin/main, dirty count, model.
# Reads Claude Code's JSON payload from stdin (jq is required); falls back
# silently when git or jq is unavailable so the prompt never breaks.
set -eu

input=$(cat 2>/dev/null || echo '{}')

if command -v jq >/dev/null 2>&1; then
  model=$(echo "$input" | jq -r '.model.display_name // .model.id // ""' 2>/dev/null || echo "")
  cwd=$(echo "$input"   | jq -r '.workspace.current_dir // .cwd // ""' 2>/dev/null || echo "")
else
  model=""
  cwd=""
fi
[ -z "$cwd" ] && cwd=$(pwd)

if ! command -v git >/dev/null 2>&1 \
  || ! git -C "$cwd" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  printf '%s' "$cwd"
  exit 0
fi

branch=$(git -C "$cwd" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "(detached)")

# Determine ahead/behind against origin/main. If origin/main isn't fetched
# locally, skip silently. We do NOT trigger a fetch from the statusline —
# that would block the prompt on network.
ahead_behind=""
if git -C "$cwd" rev-parse --verify --quiet origin/main >/dev/null; then
  set +e
  counts=$(git -C "$cwd" rev-list --left-right --count "origin/main...HEAD" 2>/dev/null)
  set -e
  if [ -n "$counts" ]; then
    behind=$(echo "$counts" | awk '{print $1}')
    ahead=$(echo "$counts"  | awk '{print $2}')
    if [ "$ahead" != "0" ] || [ "$behind" != "0" ]; then
      ahead_behind=" ↑${ahead} ↓${behind}"
    fi
  fi
fi

dirty=$(git -C "$cwd" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
dirty_part=""
[ "$dirty" != "0" ] && dirty_part=" ●${dirty}"

short_cwd=$(basename "$cwd")

if [ -n "$model" ]; then
  printf '%s %s%s%s [%s]' "$short_cwd" "$branch" "$ahead_behind" "$dirty_part" "$model"
else
  printf '%s %s%s%s' "$short_cwd" "$branch" "$ahead_behind" "$dirty_part"
fi
