#!/bin/bash
# PostToolUse(Edit|Write|MultiEdit) hook: when a code edit introduces a new
# comment line, inject a reminder asking whether that comment is necessary
# and whether it can be simpler.
#
# It does not decide — it surfaces the freshly added comment lines so the
# model re-reviews them against Beutl's comment standard (CLAUDE.md) right
# after writing them.
#
# Scope: .cs / .axaml / .xaml only. It compares comment lines in the new
# content fragments (`new_string`, `content`, `edits[].new_string`) against
# the ones being replaced (`old_string`, `edits[].old_string`) and fires only
# on comment lines that are genuinely new — preserving or reindenting an
# existing comment does not trigger it. Bash-based writes (sed, tee) are out
# of scope.
#
# Non-blocking by contract: this runs after the edit already happened, so any
# failure (missing jq, malformed JSON) degrades to silence and never disturbs
# the tool result.
set -eu

command -v jq >/dev/null 2>&1 || exit 0

input=$(cat)
file=$(printf '%s' "$input" | jq -r '.tool_input.file_path // ""' 2>/dev/null || echo "")

case "$file" in
  *.cs|*.axaml|*.xaml) ;;
  *) exit 0 ;;
esac

# A line is "comment-bearing" if it holds a C# line comment (// but not
# part of a URL like http://), a block-comment opener (/*), or a XAML
# comment opener (<!--). Leading/trailing whitespace is stripped before
# comparison so a pure reindent is not mistaken for a new comment.
comment_re='(^|[^:])//|/\*|<!--'

extract_comments() {
  # $1 = jq filter producing the text fragments
  printf '%s' "$input" \
    | jq -r "$1" 2>/dev/null \
    | grep -E "$comment_re" 2>/dev/null \
    | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//' \
    | grep -v '^$' \
    | sort -u \
    || true
}

new_comments=$(extract_comments '
  [ (.tool_input.new_string // empty),
    (.tool_input.content    // empty),
    (.tool_input.edits      // [] | .[]?.new_string // empty)
  ] | .[]')

[ -z "$new_comments" ] && exit 0

old_comments=$(extract_comments '
  [ (.tool_input.old_string // empty),
    (.tool_input.edits      // [] | .[]?.old_string // empty)
  ] | .[]')

# Keep only comment lines present in the new content but not in the old.
added=$(comm -23 \
  <(printf '%s\n' "$new_comments") \
  <(printf '%s\n' "$old_comments") \
  2>/dev/null || true)

[ -z "$added" ] && exit 0

shown=$(printf '%s\n' "$added" | head -8)
extra=$(printf '%s\n' "$added" | tail -n +9 | grep -c '' || echo 0)
[ "$extra" -gt 0 ] 2>/dev/null && shown="$shown
… (+$extra more)"

msg="This edit added comment(s) to ${file##*/}:
$shown

Re-check each against Beutl's comment standard: write a comment only to state a constraint the code itself can't show. Drop it if it restates what the next line does, says why the change is correct, or records where it came from. If it must stay, can it be shorter? Remove or tighten any that don't earn their place — no reply needed if they all already meet the bar."

jq -n --arg ctx "$msg" '{
  hookSpecificOutput: {
    hookEventName: "PostToolUse",
    additionalContext: $ctx
  }
}'
