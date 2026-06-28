#!/usr/bin/env bash
# Loop the Beutl test suite under SwiftShader until the test host crashes, capturing a kernel core dump.
# Run the container with --privileged (so core_pattern can be set) and a FRESH /dumps mount.
# Env: TEST_PROJ (default tests/Beutl.UnitTests/Beutl.UnitTests.csproj), TEST_FILTER (FQN substring),
#      MAX_RUNS (default 20).
set +e
echo "/dumps/core.%p.%e" > /proc/sys/kernel/core_pattern 2>/dev/null \
  && echo "[loop] core_pattern=$(cat /proc/sys/kernel/core_pattern)" \
  || echo "[loop] WARN: could not set core_pattern — run the container with --privileged"
ulimit -c unlimited
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export BEUTL_REQUIRE_GPU=1 XDG_RUNTIME_DIR=/tmp
# Let the kernel write a real core: createdump (DOTNET_DbgEnableMiniDump) can't attach on these
# intermittent native faults — the crashing thread is already gone, and under qemu ptrace fails outright.
export DOTNET_DbgEnableMiniDump=0 DOTNET_EnableCrashReport=0
mkdir -p /dumps
cd /work
PROJ="${TEST_PROJ:-tests/Beutl.UnitTests/Beutl.UnitTests.csproj}"
for i in $(seq 1 "${MAX_RUNS:-20}"); do
  echo "===== ITER $i $(date +%H:%M:%S) ====="
  if [ -n "$TEST_FILTER" ]; then
    dotnet test "$PROJ" --no-build -f net10.0 --filter "FullyQualifiedName~$TEST_FILTER" 2>&1 \
      | grep -E "Total:|aborted|crashed|Failed!|Passed!" | tail -2
  else
    dotnet test "$PROJ" --no-build -f net10.0 2>&1 \
      | grep -E "Total:|aborted|crashed|Failed!|Passed!" | tail -2
  fi
  CORE=$(ls /dumps/core.* 2>/dev/null | head -1)
  if [ -n "$CORE" ]; then
    echo "[loop] CORE on iter $i: $CORE ($(stat -c%s "$CORE" 2>/dev/null) bytes)"
    exit 0
  fi
  echo "[loop] iter $i clean"
done
echo "[loop] no crash in ${MAX_RUNS:-20} runs (raise MAX_RUNS, widen TEST_FILTER, or confirm BEUTL_REQUIRE_GPU path)"
exit 0
