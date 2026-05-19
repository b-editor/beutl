#!/bin/bash
# PreToolUse(Edit|Write|MultiEdit) hook: deny MIT .csproj edits that add a
# <ProjectReference> to Beutl.FFmpegWorker (GPL-3.0-or-later).
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

if printf '%s' "$new_text_oneline" | grep -Eq '<ProjectReference[^>]*Beutl\.FFmpegWorker'; then
  deny "GPL/MIT boundary violation: MIT projects must not ProjectReference Beutl.FFmpegWorker (GPL-3.0). Use Beutl.FFmpegIpc for IPC. See docs/ai-workflow/gpl-mit-boundary.md."
fi

exit 0
