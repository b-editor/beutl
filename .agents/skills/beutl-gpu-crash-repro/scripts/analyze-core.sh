#!/usr/bin/env bash
# Print a native backtrace for the newest core in /dumps. Reading a static core needs no ptrace, so gdb
# works even under qemu — but a qemu (linux/amd64-on-arm64) core is the emulator's own arm64 core, useless
# for the guest stack; the readelf check at the end flags that case.
set +e
CORE=$(ls -S /dumps/core.* 2>/dev/null | head -1)
[ -z "$CORE" ] && { echo "[analyze] no core in /dumps"; exit 1; }
echo "[analyze] core=$CORE ($(stat -c%s "$CORE" 2>/dev/null) bytes)"
command -v gdb >/dev/null 2>&1 || { apt-get update -qq >/dev/null 2>&1; apt-get install -y -qq gdb elfutils binutils >/dev/null 2>&1; }

echo "===== gdb: faulting thread + registers (the # 0 frame + x0/this is usually the whole story) ====="
gdb -q -batch -ex "set pagination off" -ex "file /usr/share/dotnet/dotnet" -ex "core $CORE" \
  -ex "bt" -ex "info registers" 2>&1 \
  | grep -vaE "^\[New LWP|No such file|warning: Can't open|warning: .* expanded|Thread debugging|Using host" \
  | head -40

echo "===== eu-stack (fallback; more tolerant of malformed notes than gdb) ====="
eu-stack --core="$CORE" -e /usr/share/dotnet/dotnet 2>&1 | head -30

echo "===== readelf notes: signal + arch sanity ====="
echo "  (NT_ARM_* on a linux/amd64 run => this is a qemu emulator core, UNUSABLE — re-run linux/arm64 NATIVE)"
readelf -n "$CORE" 2>/dev/null | grep -iE "SIGINFO|signal|NT_ARM|PRSTATUS|PRPSINFO" | head -8
