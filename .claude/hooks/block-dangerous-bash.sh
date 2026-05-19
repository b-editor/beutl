#!/bin/bash
# PreToolUse(Bash) hook: deny a small, literal set of catastrophic commands.
#
# Intentionally narrow — sophisticated bypass routes (variable expansion,
# clustered flags, refspec gymnastics, etc.) are out of scope. The hook is
# only here to catch obvious AI mistakes. Authoritative enforcement of the
# protected-branch rule lives in GitHub branch protection.
set -eu

input=$(cat)
cmd=$(echo "$input" | jq -r '.tool_input.command // ""')

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

# Block destructive `rm -rf` against root/home in their literal forms.
case "$cmd" in
  *"rm -rf /"*|*"rm -rf ~"*|*'rm -rf $HOME'*|*'rm -rf ${HOME}'*)
    deny "Destructive rm command blocked by Beutl hook (.claude/hooks/block-dangerous-bash.sh)." ;;
esac

# Block the common explicit forms of force-pushing to main/master.
case "$cmd" in
  *"git push --force origin main"*|*"git push --force origin master"* \
  |*"git push -f origin main"*|*"git push -f origin master"* \
  |*"git push --force-with-lease origin main"*|*"git push --force-with-lease origin master"*)
    deny "Force-push to main/master is blocked by Beutl hook. Use a feature branch." ;;
esac

exit 0
