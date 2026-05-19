# Beutl.FFmpegWorker — GPL-only subtree

> **License:** GPL-3.0-or-later. This is the only GPL project in the solution. **MIT code must never link against it.** See [docs/ai-workflow/gpl-mit-boundary.md](../../docs/ai-workflow/gpl-mit-boundary.md).

## Hard boundary

1. No MIT project may add a `ProjectReference` to `Beutl.FFmpegWorker.csproj`. The PreToolUse hook `.claude/hooks/check-gpl-mit-boundary.sh` denies edits that try.
2. MIT consumers reach this worker **only via IPC** through `Beutl.FFmpegIpc` (MIT). The IPC protocol is the entire API surface.
3. `<Compile Include="..\Beutl.Extensions.FFmpeg\..." Link="..." />` is the only inbound source linkage allowed, and only because those files are GPL-compatible originals living in this subtree's licence world.

When adding a feature here, picture the IPC boundary first: what message does MIT-side need to send, what response does it expect? If you cannot describe that, you are likely about to violate the boundary.

## What lives here

- `Program.cs` / `WorkerHost.cs` — entry point, host lifecycle, IPC plumbing
- `Handlers/` — one handler per IPC request type
- `Providers/` — encoder / decoder / muxer factories
- `FFmpegLoaderWorker.cs` — locates the native FFmpeg shared library at runtime
- `FFmpegWorkerCodecCacheStub.cs` — placeholder for codec capability caching

## Tests

Process-level tests live in `tests/Beutl.FFmpegIpc.Tests/` — they spawn this worker and exercise the IPC protocol. Treat that project as the contract-test surface for any change here.

## Self-contained build note

`<SelfContained>false</SelfContained>` — the worker shares the runtime with the host. The host installer is responsible for shipping the .NET runtime; the worker just needs the right RID-specific FFmpeg natives.
