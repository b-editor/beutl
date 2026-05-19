#!/bin/bash
# Stop hook: suggest /beutl-ai-self-review when the working tree currently
# carries uncommitted changes that touch the AI workflow surface (.claude/,
# AGENTS.md, CLAUDE.md, docs/ai-workflow/) or 3+ files in total.
#
# The hook deliberately ignores commits — once work is committed, it has a
# stable home and the next session sees it as "already there". This avoids
# the false-positive case where checking out or pulling someone else's
# recent commits would otherwise trigger the reminder on every Stop event.
#
# The hook only emits a `systemMessage` — it never blocks the Stop event.
# A missing `jq`, a non-git cwd, or any other failure degrades to silence.
set -eu

command -v jq  >/dev/null 2>&1 || exit 0
command -v git >/dev/null 2>&1 || exit 0
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 0

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$repo_root"

marker="$repo_root/.claude/.last-self-review"

# Working-tree changes vs HEAD (staged, unstaged, untracked, deleted, renamed).
# Status codes from `git status --porcelain` form a fixed alphabet:
#   M=modified A=added D=deleted R=renamed C=copied ?=untracked U=unmerged
# We do NOT consult `git log` — committing then resuming on the same branch
# is a session boundary, and a freshly pulled branch with someone else's
# recent commits should not be misread as work this session produced.
changed_raw=$(git status --porcelain 2>/dev/null || true)

# Extract paths, including the post-arrow target of renames.
changed=$(printf '%s\n' "$changed_raw" \
  | awk '{
      if ($1 == "R" || $1 == "RM" || $1 == "RD" || $1 == "C")
        # rename/copy: "old -> new"; we care about new
        { for (i = 1; i <= NF; i++) if ($i == "->") { print $(i+1); next } }
      else
        # everything else: path is field 2
        { print $2 }
    }' \
  | sort -u | grep -v '^$' || true)

# Also note paths that have been deleted, since they no longer exist on
# disk but still represent fresh work the user has not yet committed.
deleted=$(printf '%s\n' "$changed_raw" \
  | awk '$1 == "D" || $1 == " D" || $1 == "AD" { print $2 }' \
  | sort -u | grep -v '^$' || true)

all_touched=$changed
[ -z "$all_touched" ] && exit 0

count=$(printf '%s\n' "$all_touched" | wc -l | tr -d ' ')

ai_workflow_touched=$(printf '%s\n' "$all_touched" \
  | grep -E '^(\.claude/|AGENTS\.md$|CLAUDE\.md$|docs/ai-workflow/)' \
  || true)

needs_review=false
if [ -n "$ai_workflow_touched" ]; then
  # Any change inside the AI workflow surface is enough to warrant a review.
  needs_review=true
elif [ "$count" -ge 3 ]; then
  # Substantial task: AGENTS.md says 3+ files triggers a review.
  needs_review=true
fi

[ "$needs_review" = "true" ] || exit 0

# Suppress only when the marker is newer than every signal that would
# otherwise trigger a reminder: HEAD's commit time AND every touched file's
# mtime. A non-empty working tree (uncommitted changes) is treated as a
# fresh signal too — otherwise a long-running session that bumps the marker
# once would silence later workflow edits.
if [ -f "$marker" ]; then
  marker_mtime=$(stat -c %Y "$marker" 2>/dev/null || stat -f %m "$marker" 2>/dev/null || echo 0)
  head_time=$(git log -1 --format=%ct HEAD 2>/dev/null || echo 0)

  # If any path was deleted, treat it as a fresh signal — the user's edit
  # is real even though the file no longer exists for stat. Without this,
  # a single later deletion would suppress reminders forever.
  if [ -n "$deleted" ]; then
    latest_touched_mtime=$(date +%s)
  else
    latest_touched_mtime=0
    while IFS= read -r f; do
      [ -z "$f" ] && continue
      [ -e "$f" ] || continue
      m=$(stat -c %Y "$f" 2>/dev/null || stat -f %m "$f" 2>/dev/null || echo 0)
      [ "$m" -gt "$latest_touched_mtime" ] && latest_touched_mtime="$m"
    done <<EOF
$all_touched
EOF
  fi

  if [ "$marker_mtime" -gt "$head_time" ] \
    && [ "$marker_mtime" -gt "$latest_touched_mtime" ]; then
    # Marker postdates both the last commit and every modified file —
    # genuinely up to date, stay silent.
    exit 0
  fi
fi

if [ -n "$ai_workflow_touched" ]; then
  reason="this session touched the AI workflow surface ($(printf '%s' "$ai_workflow_touched" | head -3 | tr '\n' ' '))"
else
  reason="this session touched $count files"
fi

msg="Reminder: $reason. Consider running \`/beutl-ai-self-review\` to keep AGENTS.md, agents, skills, rules, hooks, and docs/ai-workflow in sync before wrapping up. (Run \`touch .claude/.last-self-review\` to silence until the next change.)"

jq -n --arg m "$msg" '{ systemMessage: $m }'
