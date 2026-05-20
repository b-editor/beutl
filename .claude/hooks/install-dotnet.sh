#!/bin/bash
# SessionStart hook (web only): install the .NET SDK pinned by global.json and
# warm the NuGet cache so `dotnet build / test / format` work out of the box in
# Claude Code on the web sessions.
#
# - Skipped on local machines (developers manage their own SDK there).
# - Idempotent: re-running on a cached container is fast and never appends
#   duplicate exports to $CLAUDE_ENV_FILE.
# - Best-effort: a transient network or installer failure must not block
#   session startup for non-.NET work.
set -uo pipefail

# Local sessions: nothing to do.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

project_dir="${CLAUDE_PROJECT_DIR:-$(pwd)}"
dotnet_dir="$HOME/.dotnet"

# Persist environment for the rest of the session. Guard the append so resumes
# do not duplicate lines (which would otherwise re-prepend $dotnet_dir onto
# PATH every time the session restarts).
if [ -n "${CLAUDE_ENV_FILE:-}" ] && \
   ! grep -q '^export DOTNET_ROOT=' "$CLAUDE_ENV_FILE" 2>/dev/null; then
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

# Install the SDK pinned by global.json into $dotnet_dir specifically.
# Check the actual binary path (not just `command -v dotnet`) so a system
# install does not cause us to skip and then point users at an empty
# $dotnet_dir.
if [ ! -x "$dotnet_dir/dotnet" ]; then
  echo "[install-dotnet] Installing .NET SDK pinned by global.json..." >&2
  installer=$(mktemp)
  if curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer" \
     && chmod +x "$installer" \
     && "$installer" \
          --jsonfile "$project_dir/global.json" \
          --install-dir "$dotnet_dir" \
          --no-path; then
    rm -f "$installer"
  else
    rm -f "$installer"
    echo "[install-dotnet] SDK install failed; .NET tooling will not be available this session." >&2
    exit 0
  fi
fi

if [ ! -x "$dotnet_dir/dotnet" ]; then
  echo "[install-dotnet] dotnet binary missing at $dotnet_dir; skipping restore." >&2
  exit 0
fi

echo "[install-dotnet] dotnet $("$dotnet_dir/dotnet" --version 2>/dev/null) ready at $dotnet_dir" >&2

# Warm the NuGet cache so the first build/test in the session is fast.
# Failure here must not block the session start.
(cd "$project_dir" && "$dotnet_dir/dotnet" restore Beutl.slnx) >&2 \
  || echo "[install-dotnet] dotnet restore failed; rerun manually if needed." >&2
