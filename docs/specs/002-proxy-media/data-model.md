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

Path normalization note: `AbsolutePath` is normalized via `Path.GetFullPath` and case-folded on Windows and macOS (case-insensitive filesystems by default). On Linux (case-sensitive filesystems), no case folding is applied — two references to the same file under different casing produce different fingerprints by design. Symlinks are resolved at fingerprint time (the resolved real path is stored).

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

Concrete H.264 parameters live in `ProxyPresetDefinitions` (a lookup `ProxyPreset → ProxyEncodeParameters`); the parameter table is the single source of truth referenced by both `ProxyGenerationOrchestrator` and any UI that names presets. See research R-5 for the starting values.

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
                        ├──Cancel/Failure──▶ Failed
                        └──Crash──────────▶ Partial

Ready ──Source mtime/size/path changed──▶ Stale
Ready ──User "Delete"────────────────────▶ None
Stale ──User "Regenerate"────────────────▶ Generating
Failed ──User "Regenerate"───────────────▶ Generating
Partial ──Boot scan──────────────────────▶ None (after cleanup)
```

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
    DateTime         GeneratedAtUtc,   // When generation completed (or last failure)
    DateTime         LastUsedUtc,      // Updated whenever resolver hands this proxy to a MediaReader
    string?          FailureReason);   // Set iff State == Failed
```

Composite identity for store lookup: `(Source, Preset)`. Two `ProxyEntry`s for the same source with different presets coexist (R-6).

Invariants:
- `ProxyFileRelative` is a forward-slash-separated relative path under the store root; never starts with `/`.
- `ProxyFileSizeBytes >= 0` (zero permitted only while `State` is `Generating` / `Partial`).
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
}

public readonly record struct ProxyJobProgress(double FractionComplete, TimeSpan? Eta);

public enum ProxyJobStatus { Queued, Running, Succeeded, Failed, Canceled, Skipped }
```

Lifecycle: `Queued → Running → (Succeeded | Failed | Canceled)`, plus `Queued → Skipped` for sources the orchestrator refuses to encode (per FR-020: audio-only clips, procedural / generative sources, images below the size threshold). Terminal states never transition further.

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

`false` is the default — callers that don't know about proxies (export, codec discovery, etc.) keep getting the original. Only `SceneRenderer` opts in.

### `Scene` (existing class)

Add:

```csharp
public PreviewSourceMode PreviewSourceMode { get; set; } = PreviewSourceMode.PreferProxy;
```

Persistence: serialized into the existing `Scene` JSON via the existing `JsonSerializerOptions`. Default carries forward for projects saved without the field.

### `SceneRenderer` (existing class)

Read `Scene.PreviewSourceMode` at the start of each render and stamp `MediaOptions.PreferProxy = (mode == PreferProxy)` on every `OpenMediaFile` call it issues.

### `SceneComposer` (existing class — export path)

Construct every `MediaOptions` with `PreferProxy = false` **explicitly**, regardless of the default. The explicit assignment is the safety floor for FR-002 / FR-004 against future default changes.

---

## Aggregates and services

### `IProxyStore` (interface — see [contracts/IProxyStore.md](./contracts/IProxyStore.md))

Owns:
- The on-disk `index.json` plus per-source proxy file subtree.
- In-memory map `Dictionary<(ProxyFingerprint, ProxyPreset), ProxyEntry>`.
- Atomic write protocol for proxy files and `index.json`.

### `IProxyResolver` (interface — see [contracts/IProxyResolver.md](./contracts/IProxyResolver.md))

Owns:
- The "given a source URI + chosen preset, return a usable proxy path or null" decision.
- Maintenance of `LastUsedUtc` (touch on hand-out).
- The "pinned" set of proxy paths currently being read for preview (eviction safety clause).

### `IProxyJobQueue` (interface — see [contracts/IProxyJobQueue.md](./contracts/IProxyJobQueue.md))

Owns:
- The single-consumer `Channel<ProxyJob>` plus its drain loop.
- Job lifecycle, cancellation propagation, completion event surface for UI.

### `ProxyGenerationOrchestrator` (concrete)

Owns:
- The decode-source → encode-to-preset pump driving `FFmpegEncodingControllerProxy`.
- The temp-file → atomic-rename → `IProxyStore.Register` sequence.

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

To make boot-scan recovery robust against `index.json` loss, each per-source subdirectory also contains a `meta.json` mirroring the index entry for that source's proxies. This is a write-once-per-generation file (cheap) and the authoritative source when rebuilding the index. Cost: one small file per proxied source; benefit: graceful index recovery without data loss.

---

## Concurrency notes

- `IProxyStore`'s in-memory map is guarded by an internal `lock`.
- `IProxyJobQueue` produces from any thread, consumes on its own drain loop. Job execution is async on the thread pool but bounded to 1 active job at MVP.
- `IProxyResolver.Resolve` and its "pin" / "unpin" updates use `Interlocked` / a concurrent set so the hot preview path never takes a `lock` on every frame.
- `ProxyEvictionService` runs off the UI thread; it acquires the store lock only for the candidate scan and individual deletions, releasing between.

---

## Open data-model items

- `meta.json` sidecar shape is intentionally minimal in this doc; final field list will be settled in `/speckit-tasks` and pinned in the JSON schema.
- The `ProxyEncodeParameters` table (concrete CRF / bitrate / scale numbers per preset) lives in code as `ProxyPresetDefinitions.cs` — see research R-5.
