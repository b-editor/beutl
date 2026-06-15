#!/usr/bin/env bash
# Confirm a fix: rebuild /work, then loop RUNS times asserting NO crash. Exit 1 the moment a core appears.
# Use a FRESH /dumps mount. Run --privileged. Env: TEST_PROJ, RUNS (default 30).
# At a ~1-in-6 pre-fix repro rate a few clean runs prove nothing — keep RUNS high (>=30),
# and verify on BOTH arm64 (native) and amd64 if the bug could be arch-sensitive.
set +e
echo "/dumps/core.%p.%e" > /proc/sys/kernel/core_pattern 2>/dev/null
ulimit -c unlimited
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 BEUTL_REQUIRE_GPU=1 XDG_RUNTIME_DIR=/tmp
export DOTNET_DbgEnableMiniDump=0 DOTNET_EnableCrashReport=0
mkdir -p /dumps
cd /work
PROJ="${TEST_PROJ:-tests/Beutl.UnitTests/Beutl.UnitTests.csproj}"
echo "[verify] rebuild $(date +%H:%M:%S)"
dotnet build "$PROJ" -c Debug -f net10.0 -v quiet 2>&1 | grep -iE "error" | head -5
echo "[verify] cores present at start: $(ls /dumps/core.* 2>/dev/null | wc -l) (should be 0)"
for i in $(seq 1 "${RUNS:-30}"); do
  dotnet test "$PROJ" --no-build -f net10.0 2>&1 | grep -E "Total:|aborted|crashed" | tail -1
  if [ -n "$(ls /dumps/core.* 2>/dev/null | head -1)" ]; then
    echo "[verify] STILL CRASHES on iter $i — fix incomplete"
    exit 1
  fi
  echo "[verify] iter $i clean"
done
echo "[verify] ${RUNS:-30} consecutive clean runs on $(uname -m) — fix holds"
