# Data Model: Proxy Media Workflow

**Feature**: 002-proxy-media | **Phase**: 1 | **Date**: 2026-05-20

Concrete shapes for the new types introduced under `Beutl.Media.Proxy`, plus the touched fields on `Scene` and `MediaOptions`. Types are described in C# shorthand; final naming follows the project's existing conventions. See [contracts/](./contracts/) for the public interface contracts and [proxy-index.schema.json](./contracts/proxy-index.schema.json) for the persisted JSON schema.

---

## Value types and enums

### `ProxyFingerprint` (struct, record-struct)

```csharp
public readonly record struct ProxyFingerprint(
    string AbsolutePath,      // Path.GetFullPath, case-normalized on Windows/macOS
    long   FileSizeBytes,     // > 0
    DateTime MtimeUtc);       // Kind == Utc
```

Invariants:
- `AbsolutePath` non-empty, absolute, normalized.
- `FileSizeBytes` > 0.
- `MtimeUtc.Kind == DateTimeKind.Utc`.
- Equality is structural over all three components.

Path normalization note: `AbsolutePath` is normalized via `Path.GetFullPath`, then upper-cased (`ToUpperInvariant`) when `OperatingSystem.IsWindows()` **or** `OperatingSystem.IsMacOS()` — i.e. the case-folded platforms are exactly Windows and macOS, matching their default case-insensitive volumes, so two references to the same file differing only in case share one proxy (FR-011). On Linux (case-sensitive filesystems), no case folding is applied — references differing only in case produce different fingerprints by design. Accepted MVP caveat: case-sensitivity is a per-volume property (a case-sensitive APFS volume can exist on macOS, and Windows per-directory case sensitivity can be enabled), and Beutl folds by string case rather than probing the volume; on such a case-sensitive volume the string fold can (rarely) over-merge two genuinely distinct paths into one fingerprint. This is a deliberate MVP trade-off — we do not detect per-volume case-sensitivity. Symlinks are resolved at fingerprint time (the resolved real path is stored).

Helpers:
- `static ProxyFingerprint FromFile(string path)` — `FileInfo`-driven.
- `static bool TryFromFile(string path, out ProxyFingerprint)` — non-throwing for missing files.

### `ProxyPreset` (enum)

```csharp
public enum ProxyPreset
{
    Half    = 1,   // 1/2 source resolution
    Quarter = 2,   // 1/4 source resolution (default)
    Eighth  = 3,   // 1/8 source resolution
}
```

Concrete H.264 parameters live in `ProxyPresetDefinitions` (a lookup `ProxyPreset → ProxyEncodeParameters`); the parameter table is the single source of truth referenced by the `IProxyGenerator` implementation and any UI that names presets. See research R-5 for the starting values.

### `ProxyState` (enum)

```csharp
public enum ProxyState
{
    None       = 0,   // No proxy registered or on disk
    Generating = 1,   // A job is actively encoding to *.tmp
    Ready      = 2,   // Final file present, fingerprint matches source
    Stale      = 3,   // Final file present but fingerprint differs from current source
    Failed     = 4,   // Last generation attempt failed; reason recorded
    Partial    = 5,   // Crash-recovery: orphan *.tmp file or aborted entry
}
```

State transitions (single proxy = single source × single preset):

```text
None ──Enqueue──▶ Generating ──Success──▶ Ready
                        │
                        ├──Failure────────▶ Failed
                        ├──Cancel─────────▶ None (after *.tmp cleanup)
                        └──Crash──────────▶ Partial

Ready ──Source mtime/size/path changed──▶ Stale
Ready ──User "Delete"────────────────────▶ None
Stale ──User "Regenerate"────────────────▶ Generating
Failed ──User "Regenerate"───────────────▶ Generating
Partial ──Boot scan──────────────────────▶ None (after cleanup)
```

Cancel is intentionally not a failed proxy state: the job's terminal state is
`ProxyJobStatus.Canceled`, no `FailureReason` is recorded, and the store removes
the in-flight placeholder after deleting `*.tmp`. If a regeneration was replacing
an existing `Ready` / `Stale` / `Failed` entry, cancel restores that previous
entry instead of overwriting it with `None` or `Failed`.

### `PreviewSourceMode` (enum)

```csharp
public enum PreviewSourceMode
{
    PreferProxy    = 0,   // Default. Use proxy if Ready, else fall back to original
    ForceOriginal  = 1,   // Always decode from original in preview
}
```

Stored on `Scene` (see "Touched types" below). Export path ignores this — it always decodes from original regardless.

---

## Entity records

### `ProxyEntry` (record)

```csharp
public sealed record ProxyEntry(
    ProxyFingerprint Source,           // Identity key (path, size, mtime)
    ProxyPreset      Preset,           // Which preset this file represents
    ProxyState       State,            // Lifecycle state
    string           ProxyFileRelative,// Path relative to store root, e.g. "ab12.../quarter.mp4"
    long             ProxyFileSizeBytes,
    PixelSize        OriginalLogicalFrameSize, // Original source FrameSize; logical footprint to preserve
    PixelSize        ProxyDecodedFrameSize,    // Decoded proxy FrameSize; supply-density source
    DateTime         GeneratedAtUtc,   // When generation completed (or last failure)
    DateTime         LastUsedUtc,      // Updated whenever resolver hands this proxy to a MediaReader
    string?          FailureReason);   // Set iff State == Failed
```

Composite identity for store lookup: `(Source, Preset)`. Two `ProxyEntry`s for the same source with different presets coexist (R-6).

Invariants:
- `ProxyFileRelative` is a forward-slash-separated relative path under the store root; never starts with `/`.
- `ProxyFileSizeBytes >= 0` (zero permitted only while `State` is `Generating` / `Partial`).
- `OriginalLogicalFrameSize` and `ProxyDecodedFrameSize` are non-zero for `Ready` / `Stale` entries; zero is permitted only for pre-generation / failed / partial bookkeeping.
- `ProxyDecodedFrameSize` is less than or equal to `OriginalLogicalFrameSize` on both axes for MVP H.264 downscale presets.
- `LastUsedUtc >= GeneratedAtUtc`.
- `FailureReason` is non-null iff `State == Failed`.

### `ProxyJob` (class — has progress / cancel handles)

```csharp
public sealed class ProxyJob
{
    public Guid JobId { get; }
    public ProxyFingerprint Source { get; }
    public ProxyPreset Preset { get; }
    public IProgress<ProxyJobProgress> Progress { get; }   // surfaced to UI
    public CancellationToken CancellationToken { get; }    // composed with queue-level CTS
    public ProxyJobStatus Status { get; internal set; }
    public Exception? Error { get; internal set; }         // set iff Status == Failed
    public string? StatusMessage { get; internal set; }    // required for Skipped; optional UI text for other terminals
}

public readonly record struct ProxyJobProgress(double FractionComplete, TimeSpan? Eta);

public enum ProxyJobStatus { Queued, Running, Succeeded, Failed, Canceled, Skipped }
```

Lifecycle: `Queued → Running → (Succeeded | Failed | Canceled | Skipped)`, plus `Queued → Skipped` for sources the orchestrator can reject before starting a generator (per FR-020: audio-only clips, procedural / generative sources, still images). Terminal states never transition further. `Skipped` is a job/UI result, not a persisted `ProxyState`: skipped jobs create no proxy entry and leave any existing entry unchanged.

### `ProxyStoreConfig` (new in `Beutl.Configuration`)

```csharp
public sealed class ProxyStoreConfig : ConfigurationBase
{
    public string  StoreRootPath   { get; set; }  // default: <app cache>/Beutl/proxies
    public long    MaxTotalBytes   { get; set; }  // default: 50 * 1024L * 1024 * 1024 (50 GB)
    public ProxyPreset DefaultPreset { get; set; } // default: ProxyPreset.Quarter
}
```

Validation: `MaxTotalBytes` clamped to `[5 GB, 500 GB]` on load; `StoreRootPath` resolved to an absolute path.

---

## Touched types (existing)

### `MediaOptions` (existing record)

Add one positional or property field:

```csharp
public record MediaOptions(
    MediaMode StreamsToLoad = MediaMode.AudioVideo,
    [property: Obsolete("Do not use this property.", true)]
    int SampleRate = 44100,
    bool PreferProxy = false);   // NEW
```

`false` is the default — callers that don't know about proxies (export, codec discovery, etc.) keep getting the original. Only the preview render context opts in: `SceneCompositor` seeds `CompositionContext.PreferProxy`, and `VideoSource.Resource.Update` copies that value into `MediaOptions`.

> **Role of `PreferProxy` vs the size/density data (post-003)**: `PreferProxy` is **only the on/off toggle** — it tells `DecoderRegistry.OpenMediaFile` to consult `IProxyResolver`. The information needed to preserve the logical footprint and report supply density under 003 (the original logical size + the proxy's decoded size) is **not** carried on `MediaOptions`; it rides on the resolved `ProxyResolution` (see [contracts/IProxyResolver.md](./contracts/IProxyResolver.md)) and is consumed by the source / render-node layer (see "Source logical-size decoupling" below). A bare `bool` on `MediaOptions` is therefore sufficient and stays additive — consistent with 003 FR-025, which reserves room for a future decode-target-size hint without adding one now. The trade-off (bool-toggle + side-channel sizes vs a single decode-scale hint on `MediaOptions`) is recorded in research R-11.

### `Scene` (existing class)

Add:

```csharp
public PreviewSourceMode PreviewSourceMode { get; set; } = PreviewSourceMode.PreferProxy;
```

Persistence: serialized into the existing `Scene` JSON via the existing `JsonSerializerOptions`. Default carries forward for projects saved without the field.

### `SceneRenderer` (existing class — changed by 003)

003 made the constructor **breaking**: `SceneRenderer(Scene scene, float renderScale = 1f, bool disableResourceShare = false, float maxWorkingScale = +∞)` (forwarded to `Renderer(width, height, renderScale, maxWorkingScale)`; `disableResourceShare` seeds the inner `SceneCompositor`). The renderer is **immutable per instance** and rebuilt by-replacement off the resolved `(FrameSize, OutputScale)` observable (003 FR-031). Proxy wiring fits this lifecycle without another rebuild — but note **`SceneRenderer` does not construct `MediaOptions`**. The real propagation seam is the render **context**: `CompositionContext` already carries a `DisableResourceShare` init-property (`src/Beutl.Engine/Composition/CompositionContext.cs`), seeded by `SceneCompositor` (`src/Beutl.ProjectSystem/SceneCompositor.cs`) into its inner `CompositorContext`; `MediaOptions` is actually constructed inside `VideoSource.Resource.Update` (`new(MediaMode.Video)` at `src/Beutl.Engine/Media/Source/VideoSource.cs`), which reads that context. `PreferProxy` is threaded as a **sibling context property**: the **preview** `SceneCompositor` seeds it from `Scene.PreviewSourceMode`, and `VideoSource.Resource.Update` reads it when building `MediaOptions`. Toggling the preview source mode changes only the per-source supply density (FR-023), **not** the render scale — so it invalidates the affected sources' render-cache entries and re-queues a render, but does **not** reconstruct `SceneRenderer`. (The `PreferProxy` value is read fresh inside the render work-item, like 003 reads the renderer fresh.)

### Export render context (existing path changed by 003)

Because `MediaOptions` is constructed inside `VideoSource.Resource.Update` from the render context (see above), the export safety floor is expressed at the **context** level rather than by stamping each `MediaOptions`. The export `SceneRenderer` is built by `OutputViewModel` as `new SceneRenderer(Model, renderScale, disableResourceShare: true, maxWorkingScale)` where `renderScale = Math.Max(1, SupersampleFactor.Value)` (Off / 2× / 4×) and `maxWorkingScale = WorkingScaleCeiling.Export()` (`+∞`); downsampling happens in `FrameProviderImpl.RenderCore` when `OutputScale > 1` (003 FR-034). Its `SceneCompositor` MUST NOT seed `PreferProxy` from `Scene.PreviewSourceMode` (only the preview compositor does), so the context default `false` holds and every export `MediaOptions` is constructed with `PreferProxy = false`. This is the safety floor for FR-002 / FR-004. *(Audit: grep every `MediaOptions` construction site — the sole video one is `VideoSource.Resource.Update` (`new(MediaMode.Video)`) — and assert the export render request's context never carries `PreferProxy = true`. `SceneComposer` is the audio path and constructs no video `MediaOptions` at all, so it is not an export-routing site for this feature.)*

### Source logical-size decoupling (the 003 seam — NEW in this feature)

This is the load-bearing integration with 003 for video proxies. As shipped by 003, the video source layer couples logical size to decoded size. Still images stay on the existing `ImageSource` path for the MVP because they bypass `DecoderRegistry.OpenMediaFile`.

| Type (existing) | Site | Current (003-shipped) behavior | Required proxy behavior |
|---|---|---|---|
| `SourceVideo` | `Graphics/SourceVideo.cs:139` | `r.Source.FrameSize.ToSize(1)` (logical == decoded `FrameSize`) | Return the **original** logical size when the backing decode is a proxy |
| `VideoSourceRenderNode` | `Graphics/Rendering/VideoSourceRenderNode.cs` | `Bounds = …FrameSize.ToSize(1)`; `effectiveScale: EffectiveScale.At(1f)` (hard-coded) | `Bounds` from the **original** logical size; `EffectiveScale.At(SupplyDensity)`; draw the decoded proxy bitmap into the original-footprint dest rect |

Design (to be pinned in `/speckit-tasks`): the original logical `FrameSize` and the proxy decoded `FrameSize` are carried on the resolved `ProxyResolution` (see [contracts/IProxyResolver.md](./contracts/IProxyResolver.md)) and threaded into the source `.Resource` / render node at open time, so:

- the render node's `Bounds` (and the source's logical size) use the **original** `FrameSize` — the proxy does not move or resize content (FR-021);
- the op reports `EffectiveScale.At(SupplyDensity)`, where `SupplyDensity` is computed from the actual `ProxyDecodedFrameSize / OriginalLogicalFrameSize` and may differ from a preset's nominal factor because of long-edge clamps and integer rounding (FR-022);
- the decoded proxy bitmap is drawn scaled into the original-footprint logical destination rect (the 003 FR-024 dest-rect seam — use the dest-rect draw path, not the native 1:1 `DrawBitmap` blit).

Aspect-ratio note: proxy pixel dimensions are rounded per-axis and forced even by the H.264 encoder, so the realized proxy aspect ratio can differ from the source by a sub-pixel amount. Because the bitmap is drawn into the original-footprint destination rect (above), that difference shows up as a very slight non-uniform stretch onto the original bounds — **not** a letterbox and **not** a positional shift. This is expected encoding behavior, not a footprint bug.

When the backing decode is the original (no proxy, or `PreferProxy = false`, or export), the original `FrameSize` **is** the decoded `FrameSize`, the ratio is `1.0`, and behavior is byte-identical to 003 — so this seam is purely additive and introduces no change on the original path. This is exactly the "stable intrinsic-logical-size channel" 003 deferred (`003/data-model.md` "003 scope note", 003 FR-023/FR-024, US3 / SC-007).

---

## Aggregates and services

### `IProxyStore` (interface — see [contracts/IProxyStore.md](./contracts/IProxyStore.md))

Owns:
- The on-disk `index.json` plus per-source proxy file subtree.
- In-memory map `Dictionary<(ProxyFingerprint, ProxyPreset), ProxyEntry>`.
- Atomic write protocol for proxy files and `index.json`.

### `IProxyResolver` (interface — see [contracts/IProxyResolver.md](./contracts/IProxyResolver.md))

Owns:
- The "given a source URI + chosen preset, return a usable proxy resolution or null" decision. A `ProxyResolution` carries not only the proxy file path but also the **original logical `FrameSize`** and the **proxy decoded `FrameSize`**, so the source / render-node layer can preserve the logical footprint and report the correct supply density under 003 (FR-021/FR-022).
- Maintenance of `LastUsedUtc` (touch on hand-out).
- The "pinned" set of proxy paths currently being read for preview (eviction safety clause).

### `IProxyJobQueue` (interface — see [contracts/IProxyJobQueue.md](./contracts/IProxyJobQueue.md))

Owns:
- The single-consumer `Channel<ProxyJob>` plus its drain loop.
- Job lifecycle, cancellation propagation, completion event surface for UI.
- Async enqueue back-pressure (`ValueTask<ProxyJob> EnqueueAsync(...)`) so a full bounded queue can wait without blocking the UI thread.

### `IProxyGenerator` (Engine abstraction) and `FFmpegProxyGenerator` (concrete extension implementation)

The Engine-side queue depends on an `IProxyGenerator` abstraction so `Beutl.Engine` does not reference `Beutl.Extensions.FFmpeg` / `Beutl.FFmpegIpc`.

`Beutl.Extensions.FFmpeg.Proxy.FFmpegProxyGenerator` owns:
- The decode-source → encode-to-preset pump driving `FFmpegEncodingControllerProxy`.
- The temp-file → atomic-rename → `IProxyStore.Register` sequence.
- Writing `meta.json` sidecars with the same entries persisted in `index.json`.
- Surfacing FFmpeg-missing UX through the existing `FFmpegInstallNotifier` path before returning a dependency-missing failure to the queue.

### `ProxyEvictionService` (concrete)

Owns:
- "Total bytes used vs. `MaxTotalBytes`" tracking.
- LRU candidate selection (sort by `LastUsedUtc` ascending, skipping pinned and `Generating` entries).
- Deletion of evicted files plus index update plus user-facing notification (FR-018b).

---

## Persistence model

### `index.json`

One file per store root. JSON object:

```json
{
  "version": 1,
  "entries": [ { "source": { ... }, "preset": "Quarter", "state": "Ready", ... } ],
  "lastEvictionUtc": "2026-05-20T08:00:00Z"
}
```

Full schema is in [contracts/proxy-index.schema.json](./contracts/proxy-index.schema.json). Notes:

- `version` is forward-incompatible: an unknown version triggers a directory rescan and a fresh write at the current version.
- Writes are temp-then-rename (`index.json.tmp` → `index.json`).
- Concurrent writers (theoretically possible if the user launches two Beutl instances against the same store root) are handled pessimistically: the store takes an OS file lock on a sibling `.lock` file during writes; failure to acquire degrades the second instance into read-only mode and logs a warning.

### Boot scan

On `IProxyStore` startup:
1. Parse `index.json`. On failure (corrupt, missing, version mismatch), schedule a directory rescan.
2. For every entry: verify the proxy file exists and `proxyFileSize` matches; if not, mark `State = None` and drop.
3. Find orphan `*.tmp` files; delete them.
4. Find proxy files not in the index; either register them (if their containing dir maps to a parseable fingerprint via inspection of a sidecar `meta.json` written next to the file) or delete them as garbage.

### Sidecar `meta.json` per proxy directory

To make boot-scan recovery robust against `index.json` loss, each per-source subdirectory also contains a `meta.json` mirroring the index entries for that source's proxies. This is a write-once-per-generation file (cheap) and the authoritative source when rebuilding the index. Cost: one small file per proxied source; benefit: graceful index recovery without data loss.

Shape:

```json
{
  "version": 1,
  "source": { "absolutePath": "...", "fileSizeBytes": 123, "mtimeUtc": "2026-05-20T08:00:00Z" },
  "entries": [
    {
      "source": { "absolutePath": "...", "fileSizeBytes": 123, "mtimeUtc": "2026-05-20T08:00:00Z" },
      "preset": "Quarter",
      "state": "Ready",
      "proxyFileRelative": "ab12.../quarter.mp4",
      "proxyFileSizeBytes": 123456,
      "originalLogicalFrameSize": { "width": 3840, "height": 2160 },
      "proxyDecodedFrameSize": { "width": 960, "height": 540 },
      "generatedAtUtc": "2026-05-20T08:10:00Z",
      "lastUsedUtc": "2026-05-20T08:10:00Z",
      "failureReason": null
    }
  ]
}
```

The schema is pinned in `contracts/proxy-index.schema.json` under `$defs.proxySourceMetadata`; `index.json` uses the same `proxyEntry` definition.

---

## Concurrency notes

- `IProxyStore`'s in-memory map is guarded by an internal `lock`.
- `IProxyJobQueue.EnqueueAsync` produces from any thread, consumes on its own drain loop, and awaits capacity when the bounded channel is full. Job execution is async on the thread pool but bounded to 1 active job at MVP.
- `IProxyResolver.Resolve` and its "pin" / "unpin" updates use `Interlocked` / a concurrent set so the hot preview path never takes a `lock` on every frame.
- `ProxyEvictionService` runs off the UI thread; it acquires the store lock only for the candidate scan and individual deletions, releasing between.

---

## Open data-model items

- The `ProxyEncodeParameters` table (concrete CRF / bitrate / scale numbers per preset) lives in code as `ProxyPresetDefinitions.cs` — see research R-5.
