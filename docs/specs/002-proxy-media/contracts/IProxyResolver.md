# Contract: `IProxyResolver`

**Feature**: 002-proxy-media | **Type**: internal extensibility surface

The decision layer that `DecoderRegistry.OpenMediaFile` consults when `MediaOptions.PreferProxy` is `true`. Wraps `IProxyStore` lookups with the "is this entry actually usable right now?" logic and tracks the pinned set so `ProxyEvictionService` knows which files must not be evicted.

## C# shape

```csharp
public interface IProxyResolver
{
    /// <summary>
    /// Given a source file URI and the preset to prefer, return a resolved proxy
    /// the caller can open instead of the source — or null to fall back to the original.
    /// </summary>
    /// <remarks>
    /// Returns null in every "use the original" case: source has no proxy, only stale/partial
    /// proxies, the entry's file is missing on disk, etc. The caller never sees a state value.
    /// </remarks>
    ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset);

    /// <summary>
    /// Caller has obtained the MediaReader and is about to start decoding. Pin the proxy
    /// file so ProxyEvictionService cannot delete it until ReleasePin is called.
    /// </summary>
    /// <returns>An opaque pin handle; disposing it calls ReleasePin.</returns>
    IDisposable Pin(ProxyResolution resolution);
}

/// <summary>
/// Result of a successful resolve. Carries the absolute path to the proxy file plus the
/// metadata the decoder needs to behave equivalently to opening the source.
/// </summary>
/// <remarks>
/// Post-003: <c>OriginalLogicalFrameSize</c> and <c>ProxyDecodedFrameSize</c> are REQUIRED so
/// the source / render-node layer can preserve the clip's logical footprint and report the
/// correct supply density under the 003 resolution-independent pipeline (002 FR-021 / FR-022).
/// A proxy decodes a smaller bitmap but MUST occupy the same logical area as the original;
/// without these two sizes the source would shrink to the proxy's decoded FrameSize and move
/// the content on the canvas.
/// </remarks>
public sealed record ProxyResolution(
    string AbsoluteProxyFilePath,
    ProxyFingerprint Source,
    ProxyPreset Preset,
    PixelSize OriginalLogicalFrameSize,   // the original source FrameSize = logical footprint to preserve
    PixelSize ProxyDecodedFrameSize)       // the proxy's decoded FrameSize (smaller by the preset scale)
{
    /// <summary>
    /// The supply density this proxy presents to the 003 pipeline:
    /// <c>ProxyDecodedFrameSize / OriginalLogicalFrameSize</c> (e.g. 0.5 for a Half proxy).
    /// The render node reports this as <c>EffectiveScale.At(SupplyDensity)</c> (002 FR-022).
    /// Uniform-scale presets ⇒ both axes agree; computed from the long edge.
    /// </summary>
    public float SupplyDensity
    {
        get
        {
            // Uniform-scale presets ⇒ both axes agree; compute from the long edge.
            // (PixelSize exposes int Width/Height; the exact accessor is pinned in implementation.)
            int orig = Math.Max(OriginalLogicalFrameSize.Width, OriginalLogicalFrameSize.Height);
            return orig == 0
                ? 1f
                : (float)Math.Max(ProxyDecodedFrameSize.Width, ProxyDecodedFrameSize.Height) / orig;
        }
    }
}
```

## Behavior contract

1. **Resolution policy** (in priority order):
    1. If `IProxyStore.TryGet(currentFingerprint, preferredPreset)` returns a `Ready` entry whose proxy file exists and whose size matches, return a `ProxyResolution` for it.
    2. If a `Ready` entry exists for a *different* preset, return a `ProxyResolution` for it. Rationale: a proxy of any preset is better than the original for preview speed, and the user can still re-generate at the preferred preset. **The returned `ProxyDecodedFrameSize` / `SupplyDensity` reflect the *actually-resolved* preset** (e.g. a `Half` proxy returned for a `Quarter` request reports density `0.5`, not `0.25`), so the 003 pipeline sees the real supply density; `OriginalLogicalFrameSize` is always the original source's footprint regardless of preset.
    3. Otherwise return `null` — caller falls back to the original.
6. **Size population (post-003)**: every non-null `ProxyResolution` MUST carry `OriginalLogicalFrameSize` (the original source `FrameSize`, captured at generation time and stored in the proxy metadata) and `ProxyDecodedFrameSize` (read from the proxy file's own header / the stored metadata). These are what let the source layer keep the logical footprint fixed and report `EffectiveScale.At(SupplyDensity)` (FR-021/FR-022). `Resolve` does not need to open the original file to obtain `OriginalLogicalFrameSize` — it is persisted in the proxy's `meta.json` sidecar at generation time (R-6).
2. **Pin handles**: `Pin(resolution)` must be called *before* `OpenMediaFile` on the proxy path; releasing the handle (Dispose) removes the pin. `ProxyEvictionService` consults the pin set on every eviction sweep and skips pinned proxies.
3. **Touch on resolve**: a successful `Resolve` MUST touch `LastUsedUtc` via `IProxyStore.Touch`. This keeps the LRU eviction policy aligned with actual use.
4. **No I/O for missing entries**: if `TryGet` returns null, `Resolve` returns null without stat'ing any files — preview performance must not regress for un-proxied projects.
5. **Re-entrancy**: multiple concurrent `Resolve` calls for the same source are safe and may share pin handles by reference counting.

## Test obligations (NUnit)

- `Resolve` with no entry returns null.
- `Resolve` with `Ready` entry returns a resolution; same call after touching the source file (mtime bump) returns null on the next call because the store now sees a stale entry.
- Cross-preset fallback: with only a `Half` proxy registered, `Resolve(_, Quarter)` returns the `Half` proxy, and its `SupplyDensity == 0.5` (the resolved preset's density, not the requested `0.25`); `OriginalLogicalFrameSize` equals the original source `FrameSize`.
- Size fields: a non-null resolution always carries `OriginalLogicalFrameSize` (> 0) and `ProxyDecodedFrameSize` (> 0), with `SupplyDensity` strictly on `(0, 1]`; for an original-path (no-proxy) open these are absent (caller falls back, density `1.0` is implicit).
- `Pin` lifecycle: pin handles can be acquired, released, and re-acquired idempotently; release count must equal acquire count before the proxy becomes evictable again.
- Resolver does not stat the proxy file on every call (verify via injected `IFileSystem` mock — calls bounded to `TryGet` only).
- `Touch` is called exactly once per successful `Resolve`.
