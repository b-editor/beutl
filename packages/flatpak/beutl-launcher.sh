#!/usr/bin/env bash
set -euo pipefail

beutl_bin=/app/lib/beutl/Beutl
if [ ! -x "$beutl_bin" ]; then
    echo "beutl: $beutl_bin is missing or not executable" >&2
    exit 1
fi

cd /app/lib/beutl || { echo "beutl: cannot cd to /app/lib/beutl" >&2; exit 1; }
exec "$beutl_bin" "$@"
