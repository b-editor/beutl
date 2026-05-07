#!/usr/bin/env bash
set -e

: "${XDG_DATA_HOME:=$HOME/.var/app/net.beditor.Beutl/data}"
export BEUTL_HOME="${BEUTL_HOME:-$XDG_DATA_HOME/beutl}"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-$XDG_DATA_HOME/beutl-extract}"
export DOTNET_ROOT=/app/lib/beutl

mkdir -p "$BEUTL_HOME" "$DOTNET_BUNDLE_EXTRACT_BASE_DIR"

cd /app/lib/beutl
exec /app/lib/beutl/Beutl "$@"
