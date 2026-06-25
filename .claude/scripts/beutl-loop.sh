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
#   .claude/scripts/beutl-loop.sh [until-empty | N] [bug|diff|design|feature]
#     # default: until-empty (drain the board); optional 2nd arg restricts the item kind.
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

# PROGRESS_PID must be initialized before the trap is set: an early exit (bad
# argument, refused branch) fires the EXIT trap, and `set -u` would crash
# cleanup() on an unbound reference.
PROGRESS_PID=""

# cleanup runs on exit (normal, error, signal) so the progress poster is killed
# and the final Gist summary is posted even when `claude` exits non-zero.
LOOP_RC=0
cleanup() {
  # Stop the periodic poster if it was running.
  [ -n "${PROGRESS_PID:-}" ] && kill "${PROGRESS_PID}" 2>/dev/null || true

  # Post-run: emit the final JSON run summary (H-15) to a Gist if requested (H-16).
  if [ "${BEUTL_LOOP_PROGRESS_GIST:-0}" = "1" ]; then
    LATEST_RUN_JSON=$(ls -t .claude/logs/beutl-loop-run-*.json 2>/dev/null | head -1 || true)
    if [ -n "$LATEST_RUN_JSON" ]; then
      if [ -n "${BEUTL_LOOP_PROGRESS_GIST_ID:-}" ]; then
        gh gist edit "$BEUTL_LOOP_PROGRESS_GIST_ID" "$LATEST_RUN_JSON" 2>/dev/null \
          && echo "Run summary posted to Gist $BEUTL_LOOP_PROGRESS_GIST_ID" || true
      else
        gh gist create "$LATEST_RUN_JSON" -d "beutl-loop run summary $(date +%Y%m%d-%H%M%S)" 2>/dev/null \
          && echo "Run summary posted to a new Gist (set BEUTL_LOOP_PROGRESS_GIST_ID to reuse next time)" || true
      fi
    fi
  fi
}
trap cleanup EXIT

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

# Default to draining the board; an integer arg sets a tighter budget.
BUDGET="${1:-until-empty}"
case "$BUDGET" in
  until-empty) : ;;
  ''|*[!0-9]*) echo "argument must be 'until-empty' or a positive integer (got '$BUDGET')" >&2; exit 2 ;;
  *) [ "$BUDGET" -gt 0 ] 2>/dev/null || { echo "item budget must be a positive integer (got '$BUDGET')" >&2; exit 2; } ;;
esac

# Optional kind filter (2nd arg), mirroring the skill's [bug|diff|design|feature].
# Reject unknown kinds and any extra args instead of silently dropping them.
KIND="${2:-}"
case "$KIND" in
  ''|bug|diff|design|feature) : ;;
  *) echo "kind filter must be one of: bug, diff, design, feature (got '$KIND')" >&2; exit 2 ;;
esac
if [ "$#" -gt 2 ]; then
  echo "usage: .claude/scripts/beutl-loop.sh [until-empty | N] [bug|diff|design|feature]" >&2
  exit 2
fi

# The loop derives a `<prefix>/<slug>` feature branch from the current branch, so it must start from a
# branch that yields a usable prefix. Refuse main/master, a detached HEAD, or a flat (no-"/") branch
# unless BEUTL_LOOP_BRANCH_PREFIX supplies the prefix explicitly.
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if { [ "$BRANCH" = "main" ] || [ "$BRANCH" = "master" ]; } && [ -z "${BEUTL_LOOP_BRANCH_PREFIX:-}" ]; then
  echo "refusing to run on '$BRANCH' — switch to a personal/feature branch, or set BEUTL_LOOP_BRANCH_PREFIX." >&2
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
Bash(gh:*) Bash(git:*) Bash(dotnet:*) Bash(python3:*) Bash(jq:*) Bash(mktemp:*) Bash(mkdir:*) Bash(rm:*) Bash(date:*) Bash(sleep:*) Bash(timeout:*) \
Bash(grep:*) Bash(rg:*) Bash(ls:*) Bash(cat:*) Bash(find:*) Bash(head:*) Bash(tail:*) Bash(wc:*) Bash(test:*) \
Bash(bash .claude/scripts/*:*)"

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

# Refresh origin/main before the loop: every item branches from and diffs against
# it, and a long-lived unattended checkout otherwise works off a stale ref
# (producing avoidable conflicts or re-introducing just-merged changes).
git fetch origin main --quiet || echo "warning: could not fetch origin/main; proceeding with the local ref." >&2

mkdir -p .claude/logs
LOG=".claude/logs/beutl-loop-$(date +%Y%m%d-%H%M%S).log"

echo "Launching /beutl-loop $BUDGET on branch '$BRANCH' (log: $LOG)"
echo "Low-to-moderate-risk PRs: the loop attempts an auto-squash-merge where the branch rules permit"
echo "(current code-owner rules usually leave PRs ready for your merge); higher-risk ones go to a human."

# BEUTL_LOOP_MAX_ITEMS / BEUTL_LOOP_MAX_MINUTES / BEUTL_LOOP_SETTLE_MINUTES /
# BEUTL_LOOP_PARALLEL (C-5, default 1 = sequential, max 3) are read from the env by the skill.
# BEUTL_LOOP_PROGRESS_GIST=1 (H-16): post periodic progress + the final JSON run summary to a Gist
#   for remote monitoring. BEUTL_LOOP_PROGRESS_GIST_ID=<id> edits an existing Gist; otherwise creates
#   a new one per run.

# Optional periodic progress poster (H-16): runs in the background while the loop is in flight.
if [ "${BEUTL_LOOP_PROGRESS_GIST:-0}" = "1" ]; then
  (
    while true; do
      sleep 300
      [ -f .claude/logs/beutl-loop-state.json ] || continue
      PROGRESS_FILE=$(mktemp /tmp/loop-progress.XXXX.json)
      jq '{run_id, items_processed, prs: (.prs|length), auto_merged: ([.prs[]|select(.outcome=="merged")]|length), left_for_human: ([.prs[]|select(.outcome=="left_for_human")]|length), stop_reason}' \
        .claude/logs/beutl-loop-state.json > "$PROGRESS_FILE" 2>/dev/null || true
      if [ -n "${BEUTL_LOOP_PROGRESS_GIST_ID:-}" ]; then
        gh gist edit "$BEUTL_LOOP_PROGRESS_GIST_ID" "$PROGRESS_FILE" 2>/dev/null || true
      else
        : # no existing gist id — the final summary creates one below
      fi
      rm -f "$PROGRESS_FILE"
    done
  ) &
  PROGRESS_PID=$!
fi

# Run the loop. Capture the exit code without letting `set -e` skip cleanup:
# pipefail + set -e would otherwise abort before the trap fires for a non-zero
# pipeline. Disable set -e around the pipeline, save PIPESTATUS[0] (claude's
# exit code, not tee's), then re-enable.
# Build the loop prompt: budget, optional kind filter, and the branch prefix.
# The prefix must be forwarded explicitly — on a flat/detached/main checkout the
# worker cannot derive it from the current branch.
PROMPT="/beutl-loop $BUDGET"
[ -n "$KIND" ] && PROMPT="$PROMPT $KIND"
if [ -n "${BEUTL_LOOP_BRANCH_PREFIX:-}" ]; then
  PROMPT="$PROMPT
(Headless launcher: use BEUTL_LOOP_BRANCH_PREFIX='$BEUTL_LOOP_BRANCH_PREFIX' as the feature-branch prefix for every item — do not derive it from the current branch.)"
fi

set +e
claude -p "$PROMPT" "${PERM_ARGS[@]}" 2>&1 | tee "$LOG"
LOOP_RC=${PIPESTATUS[0]}
set -e

echo "Loop exited with code $LOOP_RC"
exit "$LOOP_RC"
