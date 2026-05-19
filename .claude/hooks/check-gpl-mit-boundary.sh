#!/bin/bash
# PreToolUse(Edit|Write|MultiEdit) hook: deny MIT .csproj edits that add a
# <ProjectReference> to Beutl.FFmpegWorker (GPL-3.0-or-later).
#
# Intentionally simple: inspects only the new content fragments supplied by
# the tool call (`new_string`, `content`, `edits[].new_string`). Edits that
# rewrite a fragment of an existing reference, or Bash-based writes (sed,
# tee, `dotnet add reference`, etc.), are out of scope and rely on PR
# review + the GPL boundary doc for enforcement.
set -eu

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

if printf '%s' "$new_text" | grep -Eq '<ProjectReference[^>]*Beutl\.FFmpegWorker'; then
  jq -n '{
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason: "GPL/MIT boundary violation: MIT projects must not ProjectReference Beutl.FFmpegWorker (GPL-3.0). Use Beutl.FFmpegIpc for IPC. See docs/ai-workflow/gpl-mit-boundary.md."
    }
  }'
fi

exit 0
