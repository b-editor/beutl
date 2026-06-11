#!/bin/bash
# PreToolUse(Edit|Write|MultiEdit) hook: deny MIT .csproj edits that add a
# <ProjectReference> to Beutl.FFmpegWorker (GPL-3.0-or-later).
#
# Sanctioned exception: a build-order-only reference that carries
# ReferenceOutputAssembly="false" in the same tag (paired with an
# output-copy target) keeps the GPL assembly out of the MIT compile
# closure and is allowed. See docs/ai-workflow/gpl-mit-boundary.md.
#
# Inspects only the new content fragments supplied by the tool call
# (`new_string`, `content`, `edits[].new_string`). Bash-based writes (sed,
# tee, `dotnet add reference`, etc.) are out of scope and rely on PR
# review + the GPL boundary doc for enforcement.
#
# Fail-closed: any unexpected error (malformed JSON, missing `jq`, etc.)
# is converted to a deny by the ERR trap, so the hook can never silently
# allow when its own preconditions break.
set -euo pipefail

# Preflight: the deny path below depends on `jq`. If `jq` is missing,
# `deny` would itself fail (recursing the ERR trap) and the script would
# exit non-zero, which Claude Code's hook protocol treats as non-blocking
# — i.e. the tool call would proceed (silent allow). Emit a hardcoded
# deny JSON so the hook can fail closed even without jq.
if ! command -v jq >/dev/null 2>&1; then
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"check-gpl-mit-boundary.sh requires jq but it is not on PATH; denying to fail closed. Install jq (brew install jq / apt install jq) to restore normal hook operation."}}\n'
  exit 0
fi

deny() {
  jq -n --arg reason "$1" '{
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason: $reason
    }
  }'
  exit 0
}

# Disable further ERR trapping inside the handler so a failing `deny`
# (e.g. missing jq) cannot recurse.
trap 'trap - ERR; deny "check-gpl-mit-boundary.sh failed unexpectedly; denying to fail closed. See .claude/hooks/check-gpl-mit-boundary.sh."' ERR

input=$(cat)
file=$(printf '%s' "$input" | jq -r '.tool_input.file_path // ""')

# Only inspect .csproj edits.
case "$file" in
  *".csproj") ;;
  *) exit 0 ;;
esac

# The GPL project itself is allowed to reference Beutl.FFmpegWorker.
case "$file" in
  */Beutl.FFmpegWorker.csproj|*/Beutl.FFmpegWorker/*.csproj)
    exit 0 ;;
esac

# Collect every new content fragment the tool call wants to write.
new_text=$(printf '%s' "$input" | jq -r '
  [ (.tool_input.new_string // empty),
    (.tool_input.content    // empty),
    (.tool_input.edits      // [] | .[]?.new_string // empty)
  ] | .[]
')

# Normalise to a single line so multi-line `<ProjectReference …\n Include="…\Beutl.FFmpegWorker.csproj" />`
# tags cannot slip through the regex below.
new_text_oneline=$(printf '%s' "$new_text" | tr '\n' ' ')

# Examine each <ProjectReference …> tag that mentions Beutl.FFmpegWorker.
# Tags carrying ReferenceOutputAssembly="false" are the sanctioned
# build-order-only form; any other shape joins the MIT compile closure
# and is denied. (`|| true` keeps no-match grep exits from tripping the
# fail-closed ERR trap.)
offending=$(printf '%s' "$new_text_oneline" \
  | grep -Eo '<ProjectReference[^>]*' \
  | grep 'Beutl\.FFmpegWorker' \
  | grep -Ev 'ReferenceOutputAssembly[[:space:]]*=[[:space:]]*"[Ff]alse"' \
  || true)

if [ -n "$offending" ]; then
  deny "GPL/MIT boundary violation: MIT projects must not take a compile-closure ProjectReference to Beutl.FFmpegWorker (GPL-3.0). Use Beutl.FFmpegIpc for IPC, or the build-order-only form (ReferenceOutputAssembly=\"false\" + output-copy target). See docs/ai-workflow/gpl-mit-boundary.md."
fi

exit 0
