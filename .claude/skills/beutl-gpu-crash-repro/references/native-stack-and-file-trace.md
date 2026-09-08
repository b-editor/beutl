# Native-stack & managed-call-site techniques

Deep-dive companion to `SKILL.md`. Read when the quick path (gdb native frame) is not enough.

## Why macOS can't reproduce the imported-image crash
`RenderTarget.Create` / `VulkanTexture2D.CreateSkiaSurface` returns a **raster** (CPU) `SKSurface` on macOS,
so there is no imported `VkImage` and no GPU-path native fault. On Linux it wraps a real imported `VkImage`
in a `GRBackendRenderTarget`. Software SwiftShader (CI) exercises that GPU path; MoltenVK (dev Mac) does not.
=> This GPU-path crash is **Linux-only**; reproduce it in a Linux container. Process-exit compiler unloads are
a separate failure class and do not require an imported image or GPU device.

## Why GPU-path Docker repro uses arm64-native, never qemu x64
On Apple silicon, a `--platform linux/amd64` container runs x64 via **qemu-user**. A guest segfault dumps the
**qemu process's arm64 core** — `readelf -n` shows `NT_ARM_TLS` / `NT_ARM_PAC_MASK` and register IPs in qemu's
address space, which can't symbolize the guest x64 .NET/SwiftShader stack. Every ptrace-based tool also fails
under qemu-user: `createdump` (`DOTNET_DbgEnableMiniDump`), `gdb` attach, `dotnet test --blame-crash`.
SwiftShader ships `runtimes/linux-arm64`, so the bug almost always reproduces **arm64-native** (Docker
`--platform linux/arm64`, native on the Mac), where the core is real and gdb/eu-stack work. Keep x64 only to
confirm a suspected arch-specific behaviour.

## Getting the NATIVE frame
`scripts/analyze-core.sh` runs `gdb -batch -ex "file /usr/share/dotnet/dotnet" -ex "core <core>" -ex bt`
plus `eu-stack` (more tolerant of malformed notes); reading a static core needs no ptrace. The `# 0` frame
+ the first-arg register (arm64 `x0` = `this`) is usually the whole story, e.g.
`#0 SkCanvas::restoreToCount(int)`, `x0 = 0x0` => a null/freed `SkCanvas` receiver.

## Recognizing a process-exit compiler unload

If managed work already printed `Passed!` or a harness printed `done:`, and the faulting main thread has libc
exit handling directly below an unmapped PC, inspect dynamic-library lifetime before GPU ownership. Use
`LD_DEBUG=libs` to record each compiler DSO's load, `fini`, and link-map destruction, then compare the core PC
and argument register with offsets in that DSO. A library that registers a process-global `__cxa_atexit`
callback without an associable DSO handle can leave that callback behind after `dlclose`.

For Silk.NET shaderc, retain one `Shaderc.GetApi()` wrapper for the process lifetime while still releasing each
compiler, options, and result handle. Verify with a GPU-free child process that compiles, disposes, compiles
again, and exits normally; loop that process on each native architecture. File-tracing render calls does not
help this signature because rendering has already completed.

## Getting the MANAGED call site (when gdb shows `?? ()`)
The native frame's caller is JIT'd managed code gdb can't name. SOS would, but `dotnet-dump` /
`lldb` + `dotnet-sos` frequently **cannot bind the net10 DAC** from these cores ("Unrecognized SOS command",
"Runtime required", or lldb crashes). Don't burn time fighting the DAC — use the **file-trace**:

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
- A main-thread `exit` stack after successful managed completion => inspect unloaded compiler DSOs first.
- Watch RSS during the loop: flat memory rules out OOM; a long no-output "grind" then death suggests a
  giant software-Vulkan buffer, a hang, or just slow teardown before the fault.
- Varying "current test" across runs (or `--blame` naming a trivial non-GPU test) => the fault is on a
  background thread (`Beutl.RenderThr` / finalizer) and the named test is coincidental — a
  **race / teardown-timing** bug. Loop to reproduce, and prove a fix only with many consecutive clean runs.

## Worked example (003)
`#0 SkCanvas::restoreToCount(int)`, `x0=0x0`, `x1=1` on `Beutl.RenderThr`; file-trace tag
`DC count=1 rtdisp=False refs=2 freed=False` => `ImmediateCanvas.DisposeCore` restoring the base save on a
cached `SKCanvas` child wrapper whose Handle SkiaSharp had zeroed at teardown while the `SKSurface` was still
ref-alive. Two earlier hypotheses (deferred-draw UAF; OOM) were disproved first — see memory
`beutl-gpu-ci-crash-debugging`.
