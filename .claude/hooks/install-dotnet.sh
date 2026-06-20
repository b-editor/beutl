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

# Pin dotnet-install.sh to a known commit of dotnet/install-scripts and verify
# its SHA-256 before executing it, so a compromised CDN or a poisoned cache
# cannot run arbitrary code in the session (the old https://dot.net/v1 "latest"
# URL was fetched and executed unverified). Bump both values together — fetch
# src/dotnet-install.sh at the new commit and recompute the hash — when the
# installer needs updating.
dotnet_install_commit="6f559c420847ded38591392dafe785ad511f39f5"
dotnet_install_sha256="082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e"
dotnet_install_url="https://raw.githubusercontent.com/dotnet/install-scripts/${dotnet_install_commit}/src/dotnet-install.sh"
download_timeout_seconds=60
install_timeout_seconds=300
restore_timeout_seconds=180

# Print the SHA-256 of "$1" using whichever tool is available; print nothing if
# neither exists (the caller treats an empty result as "cannot verify").
sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

run_with_timeout() {
  timeout_seconds="$1"
  shift

  if command -v timeout >/dev/null 2>&1; then
    timeout "$timeout_seconds" "$@"
  else
    "$@"
  fi
}

# Install the SDK pinned by global.json into $dotnet_dir specifically.
# Probing with `dotnet --version` from the project dir asks the resolver to
# honour global.json, so a bumped pin or a stale pre-baked SDK that no longer
# satisfies it still triggers a (re)install instead of being treated as ready.
installed_now=0
if [ ! -x "$dotnet_dir/dotnet" ] \
   || ! (cd "$project_dir" && "$dotnet_dir/dotnet" --version >/dev/null 2>&1); then
  echo "[install-dotnet] Installing .NET SDK pinned by global.json..." >&2
  installer=$(mktemp)

  # Download, then verify the checksum before granting execute / running it.
  actual_sha=""
  if curl --connect-timeout 15 --max-time "$download_timeout_seconds" -fsSL "$dotnet_install_url" -o "$installer"; then
    actual_sha=$(sha256_of "$installer")
  fi

  if [ -z "$actual_sha" ]; then
    rm -f "$installer"
    echo "[install-dotnet] Could not download or checksum dotnet-install.sh; skipping install." >&2
    exit 0
  fi
  if [ "$actual_sha" != "$dotnet_install_sha256" ]; then
    rm -f "$installer"
    echo "[install-dotnet] dotnet-install.sh checksum mismatch (expected $dotnet_install_sha256, got $actual_sha); refusing to run." >&2
    exit 0
  fi

  if chmod +x "$installer" \
     && run_with_timeout "$install_timeout_seconds" "$installer" \
          --jsonfile "$project_dir/global.json" \
          --install-dir "$dotnet_dir" \
          --no-path; then
    rm -f "$installer"
    installed_now=1
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

# Warm the NuGet cache only when we just installed (or re-installed) the SDK.
# On a plain resume the package cache is already populated, and `dotnet build`
# performs an implicit restore lazily — running it unconditionally here would
# add several seconds to every resume and can hang on slow NuGet feeds.
if [ "$installed_now" -eq 1 ]; then
  (cd "$project_dir" && run_with_timeout "$restore_timeout_seconds" "$dotnet_dir/dotnet" restore Beutl.slnx) >&2 \
    || echo "[install-dotnet] dotnet restore failed; rerun manually if needed." >&2
else
  echo "[install-dotnet] SDK already installed; skipping restore warmup." >&2
fi
