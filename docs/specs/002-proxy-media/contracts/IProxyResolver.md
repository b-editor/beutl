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
    /// Monotonically increasing per-source version, bumped only when THAT source's own
    /// proxy entries change (registered / state-changed / deleted). Keyed on the fingerprint's
    /// resolved AbsolutePath, so a proxy change to one source never invalidates unrelated
    /// proxied sources. Drives FR-023 per-source cache invalidation.
    /// </summary>
    long GetSourceVersion(ProxyFingerprint source);

    /// <summary>
    /// Caller has resolved a proxy and must pin the proxy file before opening a MediaReader
    /// for AbsoluteProxyFilePath, so ProxyEvictionService cannot delete it during open/decode.
    /// </summary>
    /// <returns>An opaque pin handle; dispose it after the proxy MediaReader is disposed.</returns>
    IDisposable Pin(ProxyResolution resolution);

    /// <summary>
    /// True while at least one decode-lifetime pin (see Pin) is held for the file. On the
    /// interface so an IProxyResolver-dependent eviction service honors a custom resolver's
    /// pins — otherwise swapping DecoderRegistry.ProxyResolver would leave a custom
    /// resolver's proxies unprotected.
    /// </summary>
    bool IsPinned(string absoluteProxyFilePath);
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

1. **Resolution policy — densest-within-cap, densest-overall fallback.** `preferredPreset` acts as a *resolve-time density cap* in addition to its generation-time meaning: `Resolve` evaluates every preset's `Ready` entry with a matching, present proxy file and returns the densest one whose **actual decoded density** does not exceed the cap (ranking by realized density, not the preset's nominal scale, so long-edge-clamped large sources rank by the file that exists). If no usable proxy fits under the cap, the densest usable proxy overall is returned instead — denser-than-requested is still cheaper than decoding the original. If none is usable at all, return `null` — caller falls back to the original. **The returned `ProxyDecodedFrameSize` / `SupplyDensity` reflect the *actually-resolved* preset** (e.g. a `Half` proxy resolved for a `Quarter` request reports the `Half` density), so the 003 pipeline sees the real supply density; `OriginalLogicalFrameSize` is always the original source's footprint regardless of preset. `SupplyDensity` is always computed from the actual `ProxyDecodedFrameSize / OriginalLogicalFrameSize` — it is **not** exactly the preset's nominal factor, because the R-5 long-edge clamps (e.g. `Quarter` caps the long edge at 1280 px) and integer `PixelSize` rounding shift the realized ratio (an 8K source's `Quarter` proxy decodes to 1280 px ⇒ density ≈ `0.167`, not `0.25`).
2. **Size population (post-003)**: every non-null `ProxyResolution` MUST carry `OriginalLogicalFrameSize` (the original source `FrameSize`, captured at generation time and stored in the proxy metadata) and `ProxyDecodedFrameSize` (read from the proxy file's own header / the stored metadata). These are what let the source layer keep the logical footprint fixed and report `EffectiveScale.At(SupplyDensity)` (FR-021/FR-022). `Resolve` does not need to open the original file to obtain `OriginalLogicalFrameSize` — it is persisted in the proxy's `meta.json` sidecar at generation time (R-6).
3. **Pin handles**: `Pin(resolution)` must be called *before* `OpenMediaFile` on the proxy path; releasing the handle (Dispose) removes the pin. `ProxyEvictionService` consults the pin set on every eviction sweep and skips pinned proxies.
4. **Touch on resolve**: a successful `Resolve` MUST touch `LastUsedUtc` via `IProxyStore.Touch`. This keeps the LRU eviction policy aligned with actual use.
5. **No I/O for missing entries**: if `TryGet` returns null, `Resolve` returns null without stat'ing any files — preview performance must not regress for un-proxied projects.
6. **Re-entrancy**: multiple concurrent `Resolve` calls for the same source are safe and may share pin handles by reference counting.

## Test obligations (NUnit)

- `Resolve` with no entry returns null.
- `Resolve` with `Ready` entry returns a resolution; same call after touching the source file (mtime bump) returns null on the next call because the store now sees a stale entry.
- Cross-preset fallback: with only a `Half` proxy registered, `Resolve(_, Quarter)` returns the `Half` proxy, and its `SupplyDensity` equals `ProxyDecodedFrameSize / OriginalLogicalFrameSize` (the resolved `Half` preset's density, not the requested `Quarter`). Assert against the **actual** sizes the fixture populated, with a tolerance — do NOT hard-assert the literal `0.5`/`0.25`, because the R-5 long-edge clamps and integer `PixelSize` rounding mean the realized ratio is not always the preset's nominal factor (use controlled even dimensions if you want an exact `0.5`). `OriginalLogicalFrameSize` equals the original source `FrameSize`.
- Size fields: a non-null resolution always carries `OriginalLogicalFrameSize` (> 0) and `ProxyDecodedFrameSize` (> 0), with `SupplyDensity` strictly on `(0, 1]`; for an original-path (no-proxy) open these are absent (caller falls back, density `1.0` is implicit).
- `Pin` lifecycle: pin handles can be acquired, released, and re-acquired idempotently; release count must equal acquire count before the proxy becomes evictable again.
- Resolver does not stat the proxy file on every call (verify via injected `IFileSystem` mock — calls bounded to `TryGet` only).
- `Touch` is called exactly once per successful `Resolve`.
