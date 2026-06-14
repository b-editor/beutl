# GPL / MIT boundary

Beutl's main app is **MIT-licensed**; only `Beutl.FFmpegWorker` is **GPL-3.0-or-later**. The two ship as separate processes that talk to each other over IPC via `Beutl.FFmpegIpc`. This split is the license firewall — break it and the distribution model changes.

## Boundary diagram

```
┌────────────────────────────────────────┐
│ MIT side                               │
│  ├── Beutl, Beutl.Engine, Editor, ...  │
│  ├── Beutl.Extensions.FFmpeg           │  ← MIT extension host
│  └── Beutl.FFmpegIpc                   │  ← MIT IPC transport
└────────────┬───────────────────────────┘
             │  pipes + length-prefixed JSON + shared memory
┌────────────▼───────────────────────────┐
│ GPL side (separate executable)         │
│  └── Beutl.FFmpegWorker                │  ← GPL-3.0-or-later
└────────────────────────────────────────┘
```

## Invariants

1. **MIT projects must not take a compile-closure `ProjectReference` to `Beutl.FFmpegWorker`.**
   - The PreToolUse hook `.claude/hooks/check-gpl-mit-boundary.sh` denies this mechanically.
   - Sanctioned exception: a build-order-only reference carrying `ReferenceOutputAssembly="false"`, paired with a target that mirrors the worker's output next to the app (dev builds only — `src/Beutl/Beutl.csproj` uses this shape; Nuke publishes lay the worker out separately).
   - Do not look for workarounds — surface the design issue instead.

2. **All communication goes through `Beutl.FFmpegIpc`.**
   - `PipeStream` + length-prefixed JSON
   - Request correlation via `ConcurrentDictionary<int, TaskCompletionSource<...>>`

3. **Share code via `<Compile Include="..." Link="..." />`.**
   - The `Beutl.FFmpegWorker` `.csproj` link-includes specific sources from `Beutl.Extensions.FFmpeg`.
   - Linked sources must stay free of MIT-only dependencies (so they compile from both sides).

4. **FFmpeg native binaries live with the GPL side only.**
   - Do not bundle FFmpeg `.dll` / `.dylib` / `.so` into the MIT main app output.

5. **Keep the physical split at distribution.**
   - The MIT main executable and `Beutl.FFmpegWorker` ship as separate binaries.
   - Watch this when editing the installer/packaging under `nukebuild/`.

## How to add a new FFmpeg-backed feature

1. Add a **message handler** (endpoint) inside `Beutl.FFmpegWorker`.
2. Add the **protocol definition** (request / response types) to `Beutl.FFmpegIpc`.
3. Call it from `Beutl.Extensions.FFmpeg` via the IPC client.
4. Cover it with an IPC round-trip test under `tests/Beutl.FFmpegIpc.Tests/`.

If the impulse is "let's just call the FFmpeg API directly from the MIT side", that is a design red flag — add a handler to `Beutl.FFmpegWorker` instead.

## References

- Structure of `Beutl.FFmpegIpc.csproj`
- IPC round-trip tests under `tests/Beutl.FFmpegIpc.Tests/`
- Root `LICENSE` (MIT) and `LICENSE.GPL` (GPL-3.0-or-later)
