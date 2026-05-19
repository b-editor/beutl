#!/bin/bash
# Stop hook: suggest /beutl-ai-self-review when the current session has
# touched the AI workflow surface (.claude/, AGENTS.md, CLAUDE.md,
# docs/ai-workflow/) or 3+ tracked files in total, and the workflow scope
# has changed since the last self-review.
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

# All tracked changes vs HEAD (committed, staged, or unstaged). diff-tree-like.
changed=$(git status --porcelain 2>/dev/null | awk '{print $2}' )
# Include very recent commits in this session — Stop fires after each
# assistant turn so commits made in this session should also count.
recent_commits=$(git log --since="2 hours ago" --name-only --pretty=format: 2>/dev/null | sort -u | grep -v '^$' || true)

all_touched=$(printf '%s\n%s\n' "$changed" "$recent_commits" | sort -u | grep -v '^$' || true)
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

# Suppress if the marker is newer than the most-recently-modified
# AI workflow file (when scoped) or newer than HEAD's commit time.
if [ -f "$marker" ]; then
  marker_mtime=$(stat -f %m "$marker" 2>/dev/null || stat -c %Y "$marker" 2>/dev/null || echo 0)
  head_time=$(git log -1 --format=%ct HEAD 2>/dev/null || echo 0)
  if [ "$marker_mtime" -gt "$head_time" ]; then
    # Last review happened after the latest commit — assume current.
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
