# Beutl.FFmpegWorker — GPL-only subtree

> **License:** GPL-3.0-or-later. This is the only GPL project in the solution. **MIT code must never link against it.** See the [GPL/MIT boundary guide](../../docs/development/gpl-mit-boundary.md).

## Hard boundary

1. No MIT project may add a `ProjectReference` to `Beutl.FFmpegWorker.csproj`. `Beutl.PublicApiContractTests` enforces this boundary.
2. MIT consumers reach this worker **only via IPC** through `Beutl.FFmpegIpc` (MIT). The IPC protocol is the entire API surface.
3. Inbound source links are limited to the shared settings and decoding helpers currently listed in the worker project from `Beutl.Extensions.FFmpeg.Core` and `Beutl.Extensions.FFmpeg`. Do not move FFmpeg-calling worker code into an MIT project.

When adding a feature here, picture the IPC boundary first: what message does MIT-side need to send, what response does it expect? If you cannot describe that, you are likely about to violate the boundary.

## What lives here

- `Program.cs` / `WorkerHost.cs` — entry point, host lifecycle, IPC plumbing
- `Handlers/` — one handler per IPC request type
- `Decoding/` / `Encoding/` — FFmpeg-backed reader and encoder implementations
- `FFmpegLoaderWorker.cs` — locates the native FFmpeg shared library at runtime
- `FFmpegWorkerCodecCacheStub.cs` — placeholder for codec capability caching

## Tests

Process-level tests live in `tests/Beutl.FFmpegIpc.Tests/` — they spawn this worker and exercise the IPC protocol. Treat that project as the contract-test surface for any change here.

## Self-contained build note

`<SelfContained>false</SelfContained>` — the worker shares the runtime with the host. The host installer is responsible for shipping the .NET runtime; the worker just needs the right RID-specific FFmpeg natives.
