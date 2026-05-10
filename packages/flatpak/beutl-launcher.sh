#!/usr/bin/env bash
set -euo pipefail

: "${XDG_DATA_HOME:=$HOME/.local/share}"
export BEUTL_HOME="${BEUTL_HOME:-$XDG_DATA_HOME/beutl}"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-$XDG_DATA_HOME/beutl-extract}"
export DOTNET_ROOT=/app/lib/beutl

mkdir -p "$BEUTL_HOME" "$DOTNET_BUNDLE_EXTRACT_BASE_DIR" \
    || { echo "beutl: failed to create data directories under $XDG_DATA_HOME" >&2; exit 1; }

beutl_bin=/app/lib/beutl/Beutl
if [ ! -x "$beutl_bin" ]; then
    echo "beutl: $beutl_bin is missing or not executable" >&2
    exit 1
fi

cd /app/lib/beutl
exec "$beutl_bin" "$@"
