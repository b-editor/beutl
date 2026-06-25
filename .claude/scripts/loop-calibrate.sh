#!/usr/bin/env bash
#
# loop-calibrate.sh — re-classify the last ~10 merged PRs through the loop's risk
# classifier and report drift vs the human merge decisions.
#
# All of these PRs were merged by a human, so "merged" is the ground truth. The
# question is: which of them would the loop have auto-merged (low/mod) vs left for
# a human (high-risk), and does that split match the human's actual review effort?
# A PR the loop would call "low/mod auto-merge" but that a human spent significant
# review effort on = a false-low (the classifier is too permissive). A PR the loop
# would call "high-risk" but that a human rubber-stamped = a false-high (too
# conservative, but safe). Use the drift to tune the thresholds in SKILL.md step 4.
#
# This script collects the data and applies the heuristic; the final drift
# interpretation is for the human (or the AI running it) — risk classification is
# context-dependent and not fully reproducible in bash.
#
# Usage: bash .claude/scripts/loop-calibrate.sh [N]   # N = number of PRs (default 10)
# Exit: 0 always (report-only).
#
set -euo pipefail

N="${1:-10}"
case "$N" in
  ''|*[!0-9]*) N=10 ;;
esac

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "$(cd "$(dirname "$0")/../.." && pwd)")"
cd "$REPO_ROOT"

echo "loop-calibrate — re-classifying the last $N merged PRs through the loop's risk classifier"
echo

# Fetch the last N merged PRs: number, title, files changed, additions, deletions.
gh pr list --state merged --limit "$N" --json number,title,additions,deletions,changedFiles,mergeable \
  --jq '.[] | "\(.number)\t\(.title)\t\(.changedFiles)\t\(.additions)\t\(.deletions)"' \
  | while IFS=$'\t' read -r number title files add del; do
    loc=$((add + del))
    # Heuristic classifier mirroring SKILL.md step 4:
    #  - high-risk if: breaking, GPL/MIT, source-gen, persistence, large diff (>250 LOC or >8 files),
    #    or a bigger feature (feat that is not "small").
    #  - low/mod otherwise.
    # We can't fully detect GPL/MIT / source-gen / persistence from the list view, so we check the
    # commit type from the title and the diff size; the AI interpretation adds the rest.
    risk="low/mod (auto-merge)"
    reason=""
    case "$title" in
      *feat!*|*refactor!*) risk="high-risk"; reason="breaking change (!)" ;;
      *BREAKING*) risk="high-risk"; reason="breaking change (footer)" ;;
    esac
    if [ "$loc" -gt 250 ] 2>/dev/null; then risk="high-risk"; reason="diff > 250 LOC ($loc)"; fi
    if [ "$files" -gt 8 ] 2>/dev/null; then risk="high-risk"; reason="files > 8 ($files)"; fi

    # Check for GPL/MIT boundary, source-gen, persistence touches via the PR's files.
    pr_files=$(gh pr view "$number" --json files --jq '[.files[].path] | join(" ")' 2>/dev/null || echo "")
    case "$pr_files" in
      *Beutl.FFmpegWorker*) risk="high-risk"; reason="GPL/MIT boundary" ;;
      *SourceGenerators*) risk="high-risk"; reason="source generator" ;;
    esac

    printf 'PR #%s\t%-20s\tLOC=%s\tfiles=%s\t=> %s%s\n' \
      "$number" "${title:0:40}" "$loc" "$files" "$risk" "${reason:+ ($reason)}"
  done

echo
echo "All listed PRs were merged by a human. Compare each row's predicted risk to the"
echo "human's actual review effort to tune the thresholds in SKILL.md step 4."
echo "Run 'gh pr view <N> --json files,body' for deeper inspection of a specific PR."
