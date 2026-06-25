#!/bin/bash
# Diff-side GPL/MIT boundary scan.
#
# The PreToolUse hook (.claude/hooks/check-gpl-mit-boundary.sh) only inspects
# fragments from Edit/Write tool calls — it cannot scan a working tree or a
# diff retroactively (it exits immediately when .tool_input.file_path is empty,
# and explicitly excludes Bash-based writes). This script fills that gap: it
# scans the actual changed files for the two forbidden linkage forms.
#
# Usage:
#   check-gpl-mit-boundary-diff.sh <base_ref> <head_ref>   # scan diff between two refs
#   check-gpl-mit-boundary-diff.sh --files <f1> <f2> ...    # scan an explicit file list (working tree)
#
# Exit 0 = clean; exit 1 = violation(s) found (printed as file:line).
# Mirrors the hook's one sanctioned exception: src/Beutl/Beutl.csproj may
# carry a build-order-only <ProjectReference ... ReferenceOutputAssembly="false" />
# (paired with its CopyFFmpegWorkerForApp target). Every other project, and any
# <Compile Include>, is forbidden.
#
# Used by: beutl-pre-pr (step 2, --files mode), beutl-loop (step 2.5a, two-ref mode).
set -euo pipefail

MODE=""
BASE=""
HEAD=""

if [ "${1:-}" = "--files" ]; then
  MODE="files"
  shift
  # Explicit file list (working-tree mode, used by beutl-pre-pr): filter to .csproj/.cs.
  CHANGED=$(printf '%s\n' "$@" | grep -E '\.(csproj|cs)$' || true)
elif [ $# -ge 2 ]; then
  MODE="refs"
  BASE="$1"
  HEAD="$2"
  # Two-ref diff mode (used by beutl-loop): git diff already filtered by pathspec.
  CHANGED=$(git diff --name-only "$BASE..$HEAD" -- '*.csproj' '*.cs' 2>/dev/null || true)
else
  echo "usage: $0 <base_ref> <head_ref>  |  $0 --files <f1> <f2> ..." >&2
  exit 2
fi

if [ -z "$CHANGED" ]; then
  exit 0
fi

VIOLATIONS=""

for f in $CHANGED; do
  # In refs mode, read the head-side content via git show (the draft branch
  # may not be checked out in the current working tree — it lives in a
  # sub-agent worktree or as a pushed remote ref). In --files mode, read
  # from the working tree (pre-pr runs where the changes are).
  if [ "$MODE" = "refs" ]; then
    content=$(git show "$HEAD:$f" 2>/dev/null || true)
    [ -n "$content" ] || continue
  else
    [ -f "$f" ] || continue
    content=$(cat "$f")
  fi

  # The worker project itself ships FFmpegWorker linkages freely.
  case "$f" in
    */Beutl.FFmpegWorker/*|*/Beutl.FFmpegWorker.csproj) continue ;;
  esac

  # Flatten the file so multi-line MSBuild elements are caught.
  flat=$(printf '%s' "$content" | tr '\n' ' ')

  # <Compile Include="...FFmpegWorker..."> is always forbidden outside the worker.
  compile_hit=$(printf '%s' "$flat" | grep -oE '<Compile[^>]*Beutl\.FFmpegWorker' || true)

  # <ProjectReference> to FFmpegWorker: allow the sanctioned build-order-only
  # form (ReferenceOutputAssembly="false") ONLY in src/Beutl/Beutl.csproj.
  worker_refs=$(printf '%s' "$flat" | grep -oE '<ProjectReference[^>]*' \
    | grep 'Beutl\.FFmpegWorker' || true)

  case "$f" in
    */Beutl/Beutl.csproj)
      ref_hit=$(printf '%s' "$worker_refs" \
        | grep -Ev "ReferenceOutputAssembly[[:space:]]*=[[:space:]]*[\"'][Ff]alse[\"']" || true) ;;
    *)
      ref_hit=$worker_refs ;;
  esac

  if [ -n "$compile_hit$ref_hit" ]; then
    # Print every line naming a linkage element OR mentioning FFmpegWorker,
    # so multi-line cases show both halves. In refs mode, grep the git show
    # output; in files mode, grep the working-tree file.
    if [ "$MODE" = "refs" ]; then
      LOC=$(printf '%s\n' "$content" | grep -nE '(<(ProjectReference|Compile)\b|Beutl\.FFmpegWorker)' \
        | sed "s|^|$f:|" || true)
    else
      LOC=$(grep -nE '(<(ProjectReference|Compile)\b|Beutl\.FFmpegWorker)' "$f" \
        | sed "s|^|$f:|" || true)
    fi
    VIOLATIONS="${VIOLATIONS}${LOC}
"
  fi
done

if [ -n "$VIOLATIONS" ]; then
  printf 'GPL/MIT boundary violations:\n' >&2
  printf '%s' "$VIOLATIONS" >&2
  exit 1
fi

exit 0
