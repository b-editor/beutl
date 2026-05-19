#!/bin/bash
# PreToolUse(Bash) hook: auto-approve standard Beutl dotnet/nuke commands
# so contributors are not prompted for each repetitive build/test cycle.
set -euo pipefail

input=$(cat)
cmd=$(echo "$input" | jq -r '.tool_input.command // ""')

allow() {
  jq -n --arg reason "$1" '{
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "allow",
      permissionDecisionReason: $reason
    }
  }'
  exit 0
}

case "$cmd" in
  "dotnet build"*|"dotnet test"*|"dotnet format"*|"dotnet restore"*|"dotnet run"*|"dotnet tool"*|"dotnet --version"*|"./build.sh"*|"./build.ps1"*|"./build.cmd"*)
    allow "Standard Beutl dotnet/nuke command auto-approved by .claude/hooks/allow-dotnet-commands.sh." ;;
esac

exit 0
