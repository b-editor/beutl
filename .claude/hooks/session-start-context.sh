#!/bin/bash
# SessionStart hook: inject branch, recent commits, and uncommitted changes
# into Claude's context so subsequent prompts have repo state on hand.
# The hook must never abort the session — degrade silently when tools are
# missing or the cwd is not a git repository.
set -eu

# Bail out silently when essential tools are missing.
command -v jq  >/dev/null 2>&1 || exit 0
command -v git >/dev/null 2>&1 || exit 0
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 0

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "(detached)")
recent=$(git log --oneline -5 2>/dev/null || echo "(no commits)")
dirty=$( { git status --short 2>/dev/null || true; } | head -20)
if [ -z "$dirty" ]; then
  dirty="(clean working tree)"
fi

ctx=$(cat <<EOF
## Beutl repository state

- Branch: $branch
- Recent commits:
$recent
- Uncommitted changes:
$dirty
EOF
)

jq -n --arg ctx "$ctx" '{
  hookSpecificOutput: {
    hookEventName: "SessionStart",
    additionalContext: $ctx
  }
}'
