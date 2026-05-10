#!/usr/bin/env bash
set -euo pipefail

# XDG Base Directory Spec: XDG_DATA_HOME must be an absolute path; if it is unset,
# empty, or relative, fall back to the spec's default ($HOME/.local/share).
case "${XDG_DATA_HOME:-}" in
    /*) ;;
    *)  XDG_DATA_HOME="$HOME/.local/share" ;;
esac
export XDG_DATA_HOME
export BEUTL_HOME="${BEUTL_HOME:-$XDG_DATA_HOME/beutl}"
# /app is read-only in Flatpak, so redirect the .NET single-file bundle's extraction directory
# to a writable XDG location. Without this, the apphost fails to extract the embedded payload.
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-$XDG_DATA_HOME/beutl-extract}"

mkdir -p "$BEUTL_HOME" "$DOTNET_BUNDLE_EXTRACT_BASE_DIR" \
    || { echo "beutl: failed to create data directories under $XDG_DATA_HOME" >&2; exit 1; }

beutl_bin=/app/lib/beutl/Beutl
if [ ! -x "$beutl_bin" ]; then
    echo "beutl: $beutl_bin is missing or not executable" >&2
    exit 1
fi

cd /app/lib/beutl || { echo "beutl: cannot cd to /app/lib/beutl" >&2; exit 1; }
exec "$beutl_bin" "$@"
