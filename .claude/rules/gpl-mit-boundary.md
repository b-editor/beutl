---
paths:
  - "src/Beutl.FFmpegWorker/**"
  - "src/Beutl.FFmpegIpc/**"
  - "src/Beutl.Extensions.FFmpeg/**"
  - "src/Beutl.Extensions.MediaFoundation/**"
  - "src/Beutl.Extensions.AVFoundation/**"
---

# GPL / MIT boundary rules

The bulk of Beutl is **MIT-licensed**. `Beutl.FFmpegWorker` is a *separate executable* linked with **GPL-3.0-or-later** FFmpeg and lives in its own process. Everything outside that worker is MIT, including `Beutl.FFmpegIpc` (the IPC transport) and the extension hosts.

Hard rules:

1. **No compile-closure `ProjectReference` from a MIT project to `Beutl.FFmpegWorker`.** The local hook `.claude/hooks/check-gpl-mit-boundary.sh` denies edits that try to add one — do not work around it. The only sanctioned shape is a build-order-only reference (`ReferenceOutputAssembly="false"`) paired with an output-copy target, as `src/Beutl/Beutl.csproj` does for dev builds.
2. **All communication between MIT code and the GPL worker goes through `Beutl.FFmpegIpc`** (Protocol + Transport + SharedMemory). It uses `PipeStream` + length-prefixed JSON and a `ConcurrentDictionary<id, TaskCompletionSource>` for request correlation.
3. **Source files may be shared via `<Compile Include="..." Link="..." />`** so the GPL worker can reuse extension code without taking a project reference. Keep the linked file's logic free of MIT-only dependencies.
4. **Do not embed FFmpeg native binaries into the main app.** They belong only with `Beutl.FFmpegWorker`'s output.
5. **Distribution**: the main MIT executable and the GPL worker ship as separate binaries that communicate via IPC. This separation is the license firewall and must remain visible in the project layout.

If a feature seems to require a direct call into FFmpeg from a MIT project, that is a design red flag — surface it instead of routing around the boundary.
