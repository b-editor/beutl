# Rendering integration / ProjectSystem / Configuration review

Branch `yuto-trd/proxy` vs `main`. Scope: engine rendering integration, ProjectSystem, Configuration,
NodeGraph. Reviewed against the four standard axes plus the proxy-specific contracts (PreviewSourceMode
Scene->global move, proxy-vs-original resolution consistency across render/export/thumbnail, cache
invalidation on mode switch, DecoderRegistry routing, CompositionContext set-mutability).

## Severity summary

| Severity | Count |
|----------|-------|
| Blocker  | 0     |
| High     | 0     |
| Medium   | 1     |
| Low      | 1     |

## [Medium] src/Beutl.Engine/Media/Decoding/DecoderRegistry.cs:53-56 — proxy resolution runs outside the try/catch, so a resolve/pin failure kills the original-decode fallback

`OpenMediaFile` guards only the proxy *decoder-open* loop (lines 57-72) with `try/catch`. The three
steps that reach into the proxy subsystem before that — `new Uri(Path.GetFullPath(file))` (line 53),
`resolver.Resolve(sourceUri, ...)` (line 54), and `resolver.Pin(resolution)` (line 56) — are
unguarded.

Failure scenario: with `PreferProxy = true` (any preview render in PreferProxy mode), if `Resolve`
throws (it performs file fingerprinting / store lookups that can raise IO or store exceptions) or
`Pin` throws, or `new Uri`/`Path.GetFullPath` chokes on an unusual `file` value, the exception
propagates out of `OpenMediaFile` and the original-decoder fallback at line 78 is never reached. The
result is that the whole clip fails to open in preview, even though the identical original-decode path
would have opened it fine when proxy was off. This violates the feature contract that proxy is a
best-effort optimization that must never break original playback.

Recommendation: wrap the entire `if (options.PreferProxy && ProxyResolver is { } resolver)` body in a
`try/catch` that swallows (and ideally logs) any proxy-side exception and falls through to the
original-decoder loop, so proxy failures degrade to original decode rather than throwing. Note
`options with { PreferProxy = false }` on the fallback path already prevents recursion; the guard just
needs to extend to the resolve/pin steps.

## [Low] src/Beutl.Engine/Media/Decoding/DecoderRegistry.cs:12 — `ProxyResolver` is a public mutable static read on the decode hot path without synchronization

`public static IProxyResolver? ProxyResolver { get; set; }` is set once at startup
(`ProxyMediaServices.Initialize`, App.axaml.cs:130) and cleared on shutdown, and read unsynchronized
in `OpenMediaFile`. In the normal single-writer-at-startup flow this is fine, but the surface is a
plainly settable static that a plugin (or the shutdown path racing an in-flight open) could swap
mid-render, and the read/`Resolve`/`Pin` sequence is not atomic against a concurrent null-assignment
(ProxyMediaServices.cs:118-119). This clusters with the Medium finding above (same method, same proxy
entry path). Low because the set is effectively startup-only today; consider snapshotting into a local
(`if (options.PreferProxy && ProxyResolver is { } resolver)` already does capture into `resolver`, so
the main remaining exposure is the un-guarded exception, covered above) and documenting the
single-assignment expectation on the property.

## Notes on the proxy-specific contracts (verified clear)

- **PreviewSourceMode Scene->global move (`refactor!`).** No serialization back-compat concern:
  `PreviewSourceMode` never existed on `Scene` in `main` (it was introduced and relocated within this
  branch), so there is no released on-disk `Scene` field to migrate. It now lives on
  `EditorConfig.PreviewSourceMode` (default `PreferProxy`) and `ProxyStoreConfig` is round-tripped in
  `GlobalConfiguration` (add-handler/remove-handler/serialize/deserialize all updated symmetrically).
  No lingering Scene-scoped references remain (grep across `src/` and `tests/` is clean).
- **Export forces original.** `OutputViewModel` (renderer + composer) and `PlayerViewModel`'s
  save-frame renderer both pass `forceOriginalSource: true`; preview (`EditViewModel`) uses the
  4-arg overloads that default `forceOriginalSource: false`. In `CompositorContext`,
  `PreferProxy = !ForceOriginalSource && EditorConfig.PreviewSourceMode == PreferProxy`, and nested
  scenes (`SceneDrawable`/`SceneSound`) collapse to `forceOriginalSource = ForceOriginalSource ||
  !PreferProxy`, so export forces original through nested scenes too. Consistent.
- **Cache invalidation on mode switch.** `RenderNodeCache.IncrementRenderCount` now calls
  `Invalidate()` (disposes stale tiles) instead of only resetting `_cacheRejected`, so proxy-density
  tiles are dropped when the node reports changes after a mode switch. This is covered by the new test
  `IncrementRenderCount_WhenNodeChanged_ShouldInvalidateExistingCache`. Thumbnail cache is partitioned
  per mode/preset (`|original`, `|proxy:<preset>`, plus legacy `|proxy`), and
  `InvalidateThumbnailCacheKeys` drops every partition; covered by `SourceVideoThumbnailProxyTests`.
- **CompositionContext `init` -> `set`.** The change to mutable `DisableResourceShare`/
  `ForceOriginalSource`/`PreferProxy`/`PreferredProxyPreset` is required by the node-graph replay
  (`NodeGraphFilterEffectRenderNode` restores the captured flags onto a reused `_compositionContext`
  before `Evaluate`; `NodeGraphFilterEffect.Resource` captures them at build time). Render is
  single-threaded per compositor, so the shared-mutable context is acceptable; the xmldoc documents
  the rationale.
- **DrawBitmapScaled drops `pen`.** The proxy draw path (`ImmediateCanvas.cs:334`,
  `VideoSourceRenderNode.cs`) calls `DrawBitmapScaled(bitmap, dest, fill)` without a pen, but the
  non-proxy `DrawBitmap(bitmap, fill, pen)` already ignores its `pen` argument (its body only applies
  `fill`). So this is not a proxy-only regression — video sources never stroked a pen. Not a finding.
- **NUnit coverage.** New logic ships with matching tests: `SceneCompositorTests`
  (`PreviewSourceMode` -> `PreferProxy` mapping), `ProxyVideoLogicalSizeTests`,
  `SourceVideoThumbnailProxyTests`, `ElementViewModelProxyStateTests`, and the extended
  `RenderNodeCacheTests`. Conventions (`[TestFixture]`/`[Test]`/`[TestCase]`) are consistent.

## Clear

- src/Beutl.Configuration/GlobalConfiguration.cs (symmetric add/remove/serialize/deserialize)
- src/Beutl.Configuration/PreviewSourceMode.cs
- src/Beutl.Configuration/ProxyStoreConfig.cs (clamped ranges, normalized path, DefaultPreset==2 matches ProxyPreset.Quarter)
- src/Beutl.Extensibility/ProxyExtension.cs (mirrors DecodingExtension load/unload)
- src/Beutl.Engine/Composition/CompositionContext.cs
- src/Beutl.Engine/Graphics/ImmediateCanvas.cs
- src/Beutl.Engine/Graphics/Rendering/Cache/RenderNodeCache.cs (tested)
- src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs
- src/Beutl.Engine/Graphics/SourceVideo.cs
- src/Beutl.Engine/Graphics/SourceVideo.Thumbnails.cs
- src/Beutl.Engine/Media/Decoding/MediaOptions.cs
- src/Beutl.Engine/Media/Decoding/MediaReader.cs
- src/Beutl.Engine/Media/Source/VideoSource.cs (reader-reuse gating is intricate but internally consistent and covered by FR-023 tests)
- src/Beutl.NodeGraph/Composition/GraphSnapshot.cs
- src/Beutl.NodeGraph/NodeGraphFilterEffect.cs
- src/Beutl.NodeGraph/NodeGraphFilterEffectRenderNode.cs
- src/Beutl.ProjectSystem/SceneComposer.cs
- src/Beutl.ProjectSystem/SceneCompositor.cs
- src/Beutl.ProjectSystem/SceneRenderer.cs
- src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs
- src/Beutl.ProjectSystem/ProjectSystem/SceneSound.cs
- src/Beutl/App.axaml.cs (Initialize wrapped in try/catch with warning log — safe degradation)
