#!/bin/bash
# SessionStart hook (web only): install the .NET SDK pinned by global.json and
# warm the NuGet cache so `dotnet build / test / format` work out of the box in
# Claude Code on the web sessions.
#
# - Skipped on local machines (developers manage their own SDK there).
# - Idempotent: re-running on a cached container is fast.
# - Writes DOTNET_ROOT / PATH into $CLAUDE_ENV_FILE so subsequent tool calls
#   in this session inherit them.
set -euo pipefail

# Local sessions: nothing to do.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

project_dir="${CLAUDE_PROJECT_DIR:-$(pwd)}"
dotnet_dir="$HOME/.dotnet"

# Persist environment for the rest of the session.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$dotnet_dir\""
    echo "export PATH=\"$dotnet_dir:$dotnet_dir/tools:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
  } >> "$CLAUDE_ENV_FILE"
fi

export DOTNET_ROOT="$dotnet_dir"
export PATH="$dotnet_dir:$dotnet_dir/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Install the SDK pinned by global.json if it is not already present.
need_install=1
if command -v dotnet >/dev/null 2>&1; then
  if (cd "$project_dir" && dotnet --version >/dev/null 2>&1); then
    need_install=0
  fi
fi

if [ "$need_install" -eq 1 ]; then
  echo "[install-dotnet] Installing .NET SDK pinned by global.json..." >&2
  installer=$(mktemp)
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  chmod +x "$installer"
  "$installer" \
    --jsonfile "$project_dir/global.json" \
    --install-dir "$dotnet_dir" \
    --no-path
  rm -f "$installer"
fi

echo "[install-dotnet] dotnet $(dotnet --version) ready at $dotnet_dir" >&2

# Warm the NuGet cache so the first build/test in the session is fast.
# Failure here must not block the session start.
(cd "$project_dir" && dotnet restore Beutl.slnx) >&2 || \
  echo "[install-dotnet] dotnet restore failed; rerun manually if needed." >&2
