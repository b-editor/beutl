#!/usr/bin/env bash
# Build the Beutl test project inside the repro container (mounts: worktree -> /work).
# Env: TEST_PROJ (default tests/Beutl.UnitTests/Beutl.UnitTests.csproj).
set -e
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd /work
PROJ="${TEST_PROJ:-tests/Beutl.UnitTests/Beutl.UnitTests.csproj}"
echo "[build] arch=$(uname -m) $(date +%H:%M:%S) -> $PROJ"
dotnet build "$PROJ" -c Debug -f net10.0 -v minimal 2>&1 | tail -15
echo "[build] swiftshader .so in output:"
find /work -ipath "*runtimes/linux-*/native/libvk_swiftshader.so" 2>/dev/null | head -3
