#!/usr/bin/env bash
#
# beutl-loop.sh — headless ("while you sleep") launcher for the /beutl-loop board loop.
#
# This script holds NO loop logic. It only launches `/beutl-loop` with a deliberately
# conservative permission envelope. All selection / implementation / review-resolution /
# risk-gating / auto-merge logic lives in .claude/skills/beutl-loop/SKILL.md.
#
# Safety posture (see docs/ai-workflow/loop-engineering.md):
#   * Default does NOT pass --dangerously-skip-permissions, so the PreToolUse deny hooks
#     (force-push to main, rm -rf, GPL/MIT boundary) stay active during the unattended run.
#   * Refuses to run on main/master (the per-item "branch off origin/main" logic needs a
#     non-default starting branch).
#   * Has no merge path of its own — merging only ever happens inside /beutl-loop's risk gate.
#
# Usage:
#   .claude/scripts/beutl-loop.sh [until-empty | N]   # default: until-empty (drain the board)
# Env:
#   BEUTL_LOOP_MAX_ITEMS       runaway backstop for a drain, forwarded to the loop (default 50)
#   BEUTL_LOOP_MAX_MINUTES     overall wall-clock cap, forwarded to the loop (optional)
#   BEUTL_LOOP_SETTLE_MINUTES  per-PR review settle cap, forwarded to the loop (default 20 in skill)
#   BEUTL_LOOP_BRANCH_PREFIX   feature-branch prefix (e.g. yuto-trd) — REQUIRED on a flat/detached
#                              branch (one with no "/"); the loop uses it for each item's branch
#   BEUTL_LOOP_YOLO=1          opt into --dangerously-skip-permissions (NOT recommended: this
#                              disables the deny hooks). Requires an explicit N<=3 (cannot drain).
#
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

# Default to draining the board; an integer arg sets a tighter budget.
BUDGET="${1:-until-empty}"
case "$BUDGET" in
  until-empty) : ;;
  ''|*[!0-9]*) echo "argument must be 'until-empty' or a positive integer (got '$BUDGET')" >&2; exit 2 ;;
  *) [ "$BUDGET" -gt 0 ] 2>/dev/null || { echo "item budget must be a positive integer (got '$BUDGET')" >&2; exit 2; } ;;
esac

# The loop derives a `<prefix>/<slug>` feature branch from the current branch, so it must start from a
# branch that yields a usable prefix. Refuse main/master, a detached HEAD, or a flat (no-"/") branch
# unless BEUTL_LOOP_BRANCH_PREFIX supplies the prefix explicitly.
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if [ "$BRANCH" = "main" ] || [ "$BRANCH" = "master" ]; then
  echo "refusing to run on '$BRANCH' — switch to a personal/feature branch first." >&2
  exit 2
fi
if [ "$BRANCH" = "HEAD" ] && [ -z "${BEUTL_LOOP_BRANCH_PREFIX:-}" ]; then
  echo "refusing: detached HEAD — check out a '<prefix>/<name>' branch, or set BEUTL_LOOP_BRANCH_PREFIX." >&2
  exit 2
fi
case "$BRANCH" in
  */*) : ;;   # already a "<prefix>/..." branch — its prefix is reused
  *)   if [ -z "${BEUTL_LOOP_BRANCH_PREFIX:-}" ]; then
         echo "refusing: '$BRANCH' is a flat branch with no prefix to reuse." >&2
         echo "set BEUTL_LOOP_BRANCH_PREFIX=<your-prefix> (e.g. yuto-trd), or switch to a '<prefix>/<name>' branch." >&2
         exit 2
       fi ;;
esac

# Scoped allowlist (no bare `Bash`): only the commands the loop and its sub-agents need. A command
# outside this set stalls the unattended run (a SAFE failure) rather than executing. The PreToolUse
# deny hooks (force-push to main / rm -rf / GPL-MIT boundary) also remain active, because we do NOT
# skip permissions by default.
ALLOWED_TOOLS="Read Edit Write Grep Glob Task \
Bash(gh:*) Bash(git:*) Bash(dotnet:*) Bash(python3:*) Bash(jq:*) Bash(mktemp:*) Bash(date:*) Bash(sleep:*) \
Bash(grep:*) Bash(rg:*) Bash(ls:*) Bash(cat:*) Bash(sed:*) Bash(find:*) Bash(head:*) Bash(tail:*) Bash(wc:*) Bash(test:*)"

PERM_ARGS=(--allowedTools "$ALLOWED_TOOLS")
if [ "${BEUTL_LOOP_YOLO:-0}" = "1" ]; then
  # YOLO disables the deny hooks, so it must never drain the board unattended.
  case "$BUDGET" in
    until-empty) echo "BEUTL_LOOP_YOLO=1 cannot drain the board; pass an explicit N<=3." >&2; exit 2 ;;
  esac
  if [ "$BUDGET" -gt 3 ]; then
    echo "BEUTL_LOOP_YOLO=1 is capped at N<=3 (got $BUDGET)." >&2
    exit 2
  fi
  echo "!!! BEUTL_LOOP_YOLO=1: running with --dangerously-skip-permissions." >&2
  echo "!!! This DISABLES the force-push/rm/GPL-MIT deny hooks. Not recommended." >&2
  PERM_ARGS=(--dangerously-skip-permissions)
fi

mkdir -p .claude/logs
LOG=".claude/logs/beutl-loop-$(date +%Y%m%d-%H%M%S).log"

echo "Launching /beutl-loop $BUDGET on branch '$BRANCH' (log: $LOG)"
echo "Low-to-moderate-risk PRs: the loop attempts an auto-squash-merge where the branch rules permit"
echo "(current code-owner rules usually leave PRs ready for your merge); higher-risk ones go to a human."

# BEUTL_LOOP_MAX_ITEMS / BEUTL_LOOP_MAX_MINUTES / BEUTL_LOOP_SETTLE_MINUTES are read from the env by the skill.
claude -p "/beutl-loop $BUDGET" "${PERM_ARGS[@]}" 2>&1 | tee "$LOG"
