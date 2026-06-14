# Native-stack & managed-call-site techniques

Deep-dive companion to `SKILL.md`. Read when the quick path (gdb native frame) is not enough.

## Why macOS can't reproduce it
`RenderTarget.Create` / `VulkanTexture2D.CreateSkiaSurface` returns a **raster** (CPU) `SKSurface` on macOS,
so there is no imported `VkImage` and no GPU-path native fault. On Linux it wraps a real imported `VkImage`
in a `GRBackendRenderTarget`. Software SwiftShader (CI) exercises that GPU path; MoltenVK (dev Mac) does not.
=> GPU-path native crashes are **Linux-only**; you must reproduce in a Linux container.

## Why arm64-native, never qemu x64 (the load-bearing lesson)
On Apple silicon, a `--platform linux/amd64` container runs x64 via **qemu-user**. When the guest segfaults,
the kernel dumps the **qemu process's arm64 core** — `readelf -n` shows `NT_ARM_TLS` / `NT_ARM_PAC_MASK` and
register IPs in qemu's address space. That core cannot symbolize the guest x64 .NET/SwiftShader stack, and
every ptrace-based tool fails under qemu-user: `createdump` (`DOTNET_DbgEnableMiniDump`), `gdb` attach,
`dotnet test --blame-crash`. SwiftShader ships `runtimes/linux-arm64`, so the bug almost always reproduces
**arm64-native** (Docker `--platform linux/arm64`, native on the Mac) where the core is real and
gdb/eu-stack work. Keep x64 only to confirm a suspected arch-specific behaviour.

## Getting the NATIVE frame
`scripts/analyze-core.sh` runs `gdb -batch -ex "file /usr/share/dotnet/dotnet" -ex "core <core>" -ex bt`
plus `eu-stack` (more tolerant of malformed notes). Reading a static core needs no ptrace. The `# 0` frame
+ the first-arg register (arm64 `x0` = `this`) is usually the whole story, e.g.
`#0 SkCanvas::restoreToCount(int)`, `x0 = 0x0` => a null/freed `SkCanvas` receiver.

## Getting the MANAGED call site (when gdb shows `?? ()`)
The native frame's caller is JIT'd managed code that gdb can't name. SOS would, but `dotnet-dump` /
`lldb` + `dotnet-sos` frequently **cannot bind the net10 DAC** from these cores ("Unrecognized SOS command",
"Runtime required", or lldb itself crashes). Don't burn time fighting the DAC — use the **file-trace**:

1. Copy `scripts/CrashTrace.cs.txt` -> `src/Beutl.Engine/Graphics/CrashTrace.cs` (in the repro worktree only).
2. Inject `CrashTrace.Mark("<tag>")` immediately before EACH candidate native call (one `perl -0pi -e`
   over the file works when the call text is identical; use `GetType().Name` to distinguish overloads).
3. Run one loop pass; the crash leaves the last-written tag in `/dumps/lastrestore.txt`.
4. Enrich the tag with state (`refs`, `IsDisposed`, etc.) to disambiguate the mechanism, then re-run.
5. Revert the instrumentation before committing.

Use `Flush()` not `Flush(true)`: the kernel keeps the file buffer after a segfault (so the OS write
survives process death) and fsync-per-call is orders of magnitude too slow over millions of calls.

## Reading the signature of the crash
- `Test host process crashed` with NO managed stack + no obtainable dump => native fault or an OS kill.
- Watch RSS during the loop: flat memory rules out OOM; a long no-output "grind" then death suggests a
  giant software-Vulkan buffer, a hang, or (often) just slow teardown before the fault.
- Varying "current test" across runs (or `--blame` naming a trivial non-GPU test) => the fault is on a
  background thread (`Beutl.RenderThr` / finalizer); the named test is coincidental. This means it's a
  **race / teardown-timing** bug — loop to reproduce, and prove a fix only with many consecutive clean runs.

## Worked example (003)
`#0 SkCanvas::restoreToCount(int)`, `x0=0x0`, `x1=1` on `Beutl.RenderThr`; file-trace tag
`DC count=1 rtdisp=False refs=2 freed=False` => `ImmediateCanvas.DisposeCore` restoring the base save on a
cached `SKCanvas` child wrapper whose Handle SkiaSharp had zeroed at teardown while the `SKSurface` was still
ref-alive. Two earlier hypotheses (deferred-draw UAF; OOM) were disproved first — see memory
`beutl-gpu-ci-crash-debugging`.
