# Phase 1 Data Model: Resolution-Independent Rendering Pipeline

**Feature**: 003 | **Date**: 2026-05-30 | Derived from [spec.md](./spec.md) + [research.md](./research.md)

"Entities" here are the engine types and value objects the feature introduces or changes. There is **no persisted data-model change** (FR-001/SC-002: scale 1.0 == today; render scale is never serialized). All types are in `Beutl.Engine` unless noted.

---

## Value objects

### `RenderScale` *(new value type — UI/request layer)*
The user-facing preview scale selection. Lives in `Beutl` (editor) or `Beutl.Engine` request layer (pin in implementation).

| Field / member | Type | Rule |
|---|---|---|
| (enum cases) | `Full` / `Half` / `Quarter` / `FitToPreviewer` | FR-035 fixed options |
| `ToFloat(PixelSize frameSize, Size previewSurface)` | `float` | Full→1.0, Half→0.5, Quarter→0.25; FitToPreviewer→`min(previewSurface/frameSize, 1.0)` clamped ≤ 1.0 |

- **Invariants**: never serialized (FR-035/SC-002); distinct axis from `FrameCacheConfigScale` (FR-002); preview values resolve to `(0, 1]`. Export uses `1.0` or a supersample factor `> 1` (FR-034), supplied separately, not via this enum.
- **Default**: `Full`.

### Render scales (`float`) *(supply-driven — three scales)*
All `float` for v1 (the `Beutl.Graphics.Vector` primitive overloads exist for the FR-006 widening path):
- **`s_out`** (output scale) — render-request final target only (`RenderNodeContext.OutputScale`); never clamps an intermediate (FR-036).
- **`e`** (effective scale) — per-op supply density (`EffectiveScale`, below).
- **`w`** (working scale) — computed per buffer-allocating boundary via `ResolveWorkingScale` (FR-036); the scale an effect runs at and that spatial-length params multiply by (FR-008).

> **Glossary (naming)**: `Renderer.RenderScale` (on the renderer) `==` `RenderNodeContext.OutputScale` (on the context) `== s_out` — the same render-request output scale under two names (the context calls it `OutputScale` to stress it is *not* the working scale). The editor-facing **`RenderScale` enum** (`Full`/`Half`/`Quarter`/`FitToPreviewer`, FR-035) is a **distinct type** that *resolves to* `s_out` via `ToFloat`.

### `EffectiveScale` *(new value type)* — `Graphics/Rendering/EffectiveScale.cs`
`readonly record struct EffectiveScale` (as shipped: private `_bounded`/`_value`; `Unbounded`/`At(float)`/`IsUnbounded`/`Value`; `default == Unbounded`) — the supply density an op's pixels exist at. *(NOT a positional `(float Value, bool IsUnbounded)` — that form's `default` would wrongly be `At(0)`; see public-api.md.)*
| Member | Rule |
|---|---|
| `Unbounded` (static) | vector/lossless op — re-rasterizable at any target; **excluded from the supply `max`**. `default(EffectiveScale) == Unbounded` (byte-identity anchor: a plugin op ignoring the new param is safe). |
| `At(float scale)` (static) | a concrete bitmap density. |
- **Replaces** the first draft's separate `RenderNodeOperation.LosslessReRasterizable` bool — `IsUnbounded` subsumes it (one concept, one member; no contradictory "lossless but `e=2.0`" state).

### `ResolutionPolicy` *(new value type)* — `Graphics/Rendering/ResolutionPolicy.cs`
`readonly record struct ResolutionPolicy(ResolutionPolicyKind Kind, float Factor = 0f)`; `enum ResolutionPolicyKind { Inherit, ClampToOutput, Oversample }`. *(The `PreserveSource` policy was removed — it was identical to `Inherit` once the FR-036 floor was dropped; see below.)*
| Member | Rule |
|---|---|
| `Inherit` (default, `Kind=0`) | `w = baseline` where `baseline = supply>0 ? supply : s_out` — preserve input density (0.5→0.5, 2.0→2.0); a **vector-only** subtree (no concrete input) falls back to `s_out`. `s_out` is not a ceiling. |
| `ClampToOutput` | `w = min(supply, s_out)` — perf opt-out. |
| `Oversample(factor)` | `w = max(supply, factor × s_out)` — quality/SSAA opt-in. `factor` must be > 0 (the `Oversample(factor)` factory throws otherwise). |
- Declared via `virtual FilterEffect.ResolutionPolicy` (default `Inherit`). *(As shipped: only `FilterEffect.ResolutionPolicy` exists — a `RenderNode.ResolutionPolicy` was intentionally NOT added; it would have been a dead duplicate. See public-api.md.)* `RenderNodeContext.ResolveWorkingScale(ReadOnlySpan<EffectiveScale> inputs, float outputScale, ResolutionPolicy policy, float maxWorkingScale = +∞) → float` is the one rule, with a final `min(·, maxWorkingScale)` global ceiling (FR-037). **As shipped (T061): the FR-037 ceiling IS wired — `FilterEffectRenderNode` passes `context.MaxWorkingScale`, which the editor preview seeds at `2 × s_out` and export leaves at `+∞`. The FR-036 cross-boundary `PreserveSource` floor was removed entirely (see below) rather than wired.**
- **FR-036 (`PreserveSource` floor) removed**: an earlier draft routed a per-pull `PreserveFloor` so a `PreserveSource` descendant could lower-bound an ancestor `ClampToOutput`. It was never wired and no built-in uses `ClampToOutput`, so `PreserveSource` was behaviorally identical to `Inherit` (which already runs at the input supply density). Rather than add a floor channel to the core scale path for zero built-in benefit, the policy, its 11 effect overrides, and the `preserveFloor` parameter were all removed.

### Shared rounding helper (FR-007)
**Decision: there is no new helper type — the canonical helper *is* `PixelRect.FromRect(Rect, float scale)` / `PixelSize.FromSize(Size, float)`** (`PixelRect.cs:391`, `PixelSize.cs:209`), which already implement "ceil sizes (ceil'd bottom-right), toward-zero origins". The work is *adopting* the `× w` scaling at every sink with a consistent convention, not writing a new helper.
- **Invariant (byte-equality)**: origins round **toward zero** (`(int)` cast), NOT floor; extents **ceil**. **At `w = 1.0` each sink preserves its current rounding**: the main rasterization sink already uses `PixelRect.FromRect` (unchanged at scale 1); the two filter-effect sinks **keep their component-wise `(int)Width`/`(int)Height` truncation at `w = 1.0`** and apply `ceil(× w)` only for `w ≠ 1.0` — they are NOT unified with `FromRect` at scale 1 (that would change scale-1.0 output and break byte-identity). Golden-test the filter-target paths at `w = 1.0` (FR-005/FR-007).

---

## Changed core render-graph types

### `RenderNodeContext` *(changed)* — `Graphics/Rendering/RenderNodeContext.cs`
| Member | Change | Rule |
|---|---|---|
| `OutputScale` | **+ `float OutputScale { get; }`** (get-only, default `1f`; renamed from the first draft's `Scale`) | The render-request final target `s_out` (D1). Seeded once in `RenderNodeProcessor.Pull` from `RenderNodeProcessor.OutputScale`; propagated like `IsRenderCacheEnabled`. **Consumed only at the root final stage and as the fallback/ceiling term of `ResolveWorkingScale` — never as an intermediate's working scale.** Get-only. |
| `ResolveWorkingScale` | **+ `static float ResolveWorkingScale(ReadOnlySpan<EffectiveScale> inputs, float outputScale, ResolutionPolicy policy, float maxWorkingScale = +∞)`** (static only — no instance overload was shipped) | The one working-scale rule incl. the global ceiling (FR-037), which `FilterEffectRenderNode` now supplies via `context.MaxWorkingScale` (preview `2 × s_out`, export `+∞`). The `preserveFloor` parameter was removed with FR-036. |

### `RenderNodeOperation` *(changed)* — `Graphics/Rendering/RenderNodeOperation.cs`
| Member | Change | Rule |
|---|---|---|
| `EffectiveScale` | **+ `EffectiveScale EffectiveScale { get; }`** (read-only value type, default `Unbounded`) | The supply density `e` (D1). `Unbounded` = vector/lossless (regenerate at any `w`); `At(s)` = concrete bitmap density. Set for bitmap-backed ops (`CreateFromRenderTarget`/`CreateFromSurface`, cached tiles, decoded media, 3D & nested-scene surfaces) = their `w`. |
| factory params | factories (`CreateLambda`/`CreateFromRenderTarget`/`CreateFromSurface`/`CreateDecorator`) gain `EffectiveScale effectiveScale = default` (default = `Unbounded`) | |

- **`LosslessReRasterizable` (bool) is removed** — `EffectiveScale.IsUnbounded` subsumes it (one concept, one member).
- **Relationships**: the compositor computes `targetScale = max(concrete child EffectiveScale)` (Unbounded children excluded); off-target bitmap ops are Mitchell-resampled and `Unbounded` ops re-rasterized at `targetScale`, once at the `DrawSurface`/`DrawRenderTarget` blit (FR-017). The cap to `s_out` is **deferred to the root** (FR-016/FR-036).
- **Non-abstract (compat-critical)**: `EffectiveScale` is added as a **non-abstract** member defaulting to `Unbounded` (via the base ctor / factory param) — **never `abstract`**. The base already has three abstract members (`Bounds`/`Render`/`HitTest`, `RenderNodeOperation.cs:11-15`); an *abstract* scale member would break every in-tree subclass (the private `LambdaRenderNodeOperation`) **and every out-of-tree plugin op**. With the `Unbounded` default, a plugin op that ignores scale re-rasterizes at `w` and is byte-identical at `s_out=1.0`.
- **SaveLayer-based containers carry NO scale**: `OpacityRenderNode`/`BlendModeRenderNode`/`OpacityMaskRenderNode` are `CreateDecorator` wrappers that `PushOpacity`/`PushBlendMode`/`SaveLayer` at **render time** (`OpacityRenderNode.cs:21-28`, `ImmediateCanvas.cs:369-377`) — they do **not** allocate a node-owned `RenderTarget` from `RenderNodeContext`. They need no scale field: the `SaveLayer` captures at the current device CTM (which already carries the root `s`), and any genuinely mixed-scale child is resampled at *its own* `DrawSurface`/`DrawRenderTarget` blit inside the layer (FR-017). Only nodes that **allocate** an intermediate from `RenderNodeContext` (filter targets, brush/tile intermediates, cache tiles, nested-scene/3D surfaces) carry and consume scale.

### `RenderNodeProcessor` *(changed)* — `Graphics/Rendering/RenderNodeProcessor.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | **+ `float outputScale = 1f`**; **+ `float OutputScale { get; }`** | Seeded from `Renderer`. |
| three sinks (`:26,52,75`) | compute `w = RenderNodeContext.ResolveWorkingScale(childScales, OutputScale, node.ResolutionPolicy)`; `PixelRect.FromRect(op.Bounds, w)`; pre-push `Matrix.CreateScale(w)`; emit `e = At(w)`. **Equal-scale short-circuit**: when all child `e == w`, take today's path (no resample wrapper) — preserves byte-identity. | Identity at `w=1.0` → byte-equal. |
| `Pull` (`:121`) | `new RenderNodeContext(input, OutputScale)` | Single production construction site; reaches all overrides. |
| `RasterizeAt(op, w)` | **+ internal seam** generalizing `RasterizeToRenderTargets` (`:20-44`) to re-rasterize an `Unbounded` subtree at a chosen `w` | feeds FR-017 regenerate. |

### `RenderNodeCache` — `Graphics/Rendering/Cache/RenderNodeCache.cs`
**NOT changed in 003 (deferred — T025 `[~]`).** FR-020 scale invalidation is satisfied at the **manager** level instead: `EditViewModel` rebuilds a fresh `SceneRenderer` **and** `FrameCacheManager` atomically when the resolved `(FrameSize, OutputScale)` changes (`DistinctUntilChanged` + `DisposePreviousValue`), and the per-renderer `RenderNode` cache lives inside the renderer, so it is discarded with the old renderer — a cache can never be served at a different scale within one renderer (`OutputScale`/`RenderScale` are immutable per instance). The supply-aware **reuse** below was specced but **not shipped**:

> *Deferred (supply-aware reuse, defense-in-depth):* a `CachedWorkingScale` on `RenderNodeCache` + a `workingScale` param on `StoreCache` + `RenderCacheRules.Match` thresholds `÷ CachedWorkingScale²`, so an SSAA export could **reuse** a high preview cache (Mitchell-downsample) and **miss** only when it lacks detail (D6). Lands with the scale-keyed reuse work in a follow-up, not 003. `RenderNodeCacheHelper.CreateDefaultCache` still builds cache processors with `new RenderNodeProcessor(node, false)` (no scale) today.

### `ImmediateCanvas` *(changed)* — `Graphics/ImmediateCanvas.cs`
| Member | Change | Rule |
|---|---|---|
| `DrawSurface`/`DrawRenderTarget` (`:106-126`) | **+ `(src, dest, SKSamplingOptions)` resample path** via `Canvas.DrawImage(...,Mitchell)` for the FR-017 mixed-scale blit. **Branch on exact `srcScale == destScale` → today's bare 1:1 `Canvas.DrawSurface(...,paint)`** (byte-identity); only the `≠` case routes through the resampler. | FR-017; byte-identity-critical short-circuit. |

---

## Changed renderer / request types

### `IRenderer` *(changed)* — `Graphics/Rendering/IRenderer.cs`
| Member | Change | Rule |
|---|---|---|
| `RenderScale` | **+ `float RenderScale { get; }`** (default-interface-impl → `1f` to soften third-party impls, mirroring the `GetBoundary` default at `:30`) | |
| `DeviceSize` | **+ `PixelSize DeviceSize { get; }`** = `ceil(FrameSize × RenderScale)` | FR-003/FR-026. |

### `Renderer` *(changed)* — `Graphics/Rendering/Renderer.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | `(int width, int height)` → **`(int width, int height, float renderScale = 1f)`** | width/height stay **logical** FrameSize; device surface = `ceil(FrameSize × renderScale)`. BREAKING (FR-028). |
| `RenderScale` | **+ `float RenderScale { get; }`** | Immutable per instance (D4). |
| `FrameSize` | unchanged (logical) | |
| `Render`/`RenderDrawable` | push one `Matrix.CreateScale(renderScale)` after `_immediateCanvas.Push()` | |
| `HitTest`/`RecalculateBoundaries` | pass `1f` | Render scale stays out of hit-test/handle math (FR-027). |

### `SceneRenderer` *(changed)* — `Beutl.ProjectSystem/SceneRenderer.cs`
| Member | Change |
|---|---|
| ctor | **+ `float renderScale = 1f`** forwarded to `base(scene.FrameSize.W, .H, renderScale)`; keep `scene.FrameSize` as the logical size. BREAKING. |

### `CompositionFrame` — `Beutl.Engine/Composition/CompositionFrame.cs`
**NO CHANGE.** `Size` already is the logical frame size; render scale stays a render-request property (FR-002).

### `GraphicsContext2D` *(changed)* — `Graphics/Rendering/GraphicsContext2D.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | **+ `float outputScale = 1f`**; **+ `float OutputScale { get; }`** | exposes `s_out`; the backdrop op itself stays logical (capture-scale model below). |
| `DrawBackdrop` (`:357`) | **stays** `new Rect(canvasSize.ToSize(1))` — the snapshot records its capture scale (`ImmediateCanvas.Snapshot` → `TmpBackdrop`) and the replay un-scales by *that*, so the node bounds stay logical and `outputScale` is **not** applied here | FR-021 scale-aware backdrop (capture-scale model). |

---

## Changed filter-effect types

### `FilterEffectContext` *(changed)* — `Graphics/FilterEffects/FilterEffectContext.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | **+ `float outputScale, float workingScale`** | `workingScale` = the negotiated `w` (from `FilterEffectRenderNode` via `ResolveWorkingScale`); `outputScale` = `s_out`. |
| `WorkingScale` | **+ `float WorkingScale { get; }`** | FR-015 read accessor — the `w` the effect runs at. |
| `OutputScale` | **+ `float OutputScale { get; }`** | the eventual delivery target, for effects that need it. |
| Skia `SKImageFilter` primitives (Blur/DropShadow/Dilate/Erode/MatrixConvolution/Transform) | **NOT** multiplied by `WorkingScale` — they ride the `CreateScale(w)` CTM in `FilterEffectActivator.Flush`, so Skia scales their params for free; multiplying here would double-scale. Only **CustomEffect point-blit** code (InnerShadow, Mosaic, ColorShift, …) multiplies absolute-length args by `WorkingScale` | FR-009. |

### `CustomFilterEffectContext` *(changed)* — `Graphics/FilterEffects/CustomFilterEffectContext.cs`
| Member | Change | Rule |
|---|---|---|
| `WorkingScale` | **+ `float WorkingScale { get; }`** (renamed from `RenderScale`) | FR-015 accessor for custom/shader effects. |
| `CreateTarget(Rect)` | size `ceil(bounds × WorkingScale)` for `w ≠ 1.0`, keeping component-wise `(int)` at `w = 1.0` (byte-identity); `Open` returns a `WorkingScale`-prescaled canvas | FR-009/FR-007. |

### `FilterEffectActivator` *(changed)* — `Graphics/FilterEffects/FilterEffectActivator.cs`
`Flush` (`:23`) sizes targets `ceil(OriginalBounds × w)` for `w ≠ 1.0`, **keeping the current component-wise `(int)Width`/`(int)Height` truncation at `w = 1.0`** (byte-identity); pushes `Matrix.CreateScale(w)` and tags each flushed buffer `EffectiveScale.At(w)`. `w` (and `s_out`) are supplied to the `FilterEffectActivator` ctor (from `FilterEffectRenderNode` via `ResolveWorkingScale`), not derived from the targets. Scale-1.0-sensitive (golden-tested).

### `EffectTarget` *(changed)* — `Graphics/FilterEffects/EffectTarget.cs`
| Member | Change | Rule |
|---|---|---|
| `Scale` | **+ `EffectiveScale Scale { get; set; }`** (default `Unbounded`) | Per-intermediate supply density, set from the producing op's `e`, so divergent-scale inputs normalize to `w` before a shared filter/flatten (FR-019; LayerEffect/DelayAnimation/InnerShadow/Blend/Mosaic). Propagated through `Clone`/flush re-wrap. |
| `Empty`/`Size` | **removed** (obsolete) | Per AGENTS.md no-shim policy. |

`EffectTargets`: no scale accessor — `w` is resolved once by `RenderNodeContext.ResolveWorkingScale` and threaded through the activator, so the targets do not derive it. (Earlier drafts added `MaxScale()`/`ResolveScale(...)`; both were dropped.) `CalculateBounds` (`:27`) stays logical (scale-invariant).

---

## Changed media / drawable types

| Type | File | Change | Rule |
|---|---|---|---|
| `SourceImage` | `Graphics/SourceImage.cs:26` | `Source.FrameSize.ToSize(1)` → logical size decoupled from decoded pixel size; node Bounds logical | FR-023 |
| `SourceVideo` | `Graphics/SourceVideo.cs:139` | same | FR-023 |
| `Image/VideoSourceRenderNode` | `Graphics/Rendering/*` | draw at native pixel extent under the active CTM; tag `EffectiveScale.At(1)` (logical == decoded `FrameSize` in 003, so the ratio is exactly 1). A per-frame `decodedPixels / logicalSize` density arrives with proxy decode (scope note line 169). | FR-024 |
| `MediaOptions` | `Media/Decoding/MediaOptions.cs` | **unchanged in 003**; kept additively extensible for a future decode-scale hint | FR-025 |

> **003 scope note (logical vs decoded size)**: `ImageSource.Resource.FrameSize` today = the **decoded pixel size** (`new PixelSize(counter.Width, counter.Height)`, `ImageSource.cs:93`); likewise `VideoSource`. Because proxy decode is out of scope (FR-025), in 003 a source's **logical size == its full decoded `FrameSize`** — no separate intrinsic-logical-size channel is added. FR-023/FR-024 establish only the *seam*: a source draws into a `logicalSize × s` destination rect (not a native-px 1:1 blit), so a **future** reduced-decode supply can shrink the decoded bitmap while the logical footprint stays fixed. Pointing a source directly at a smaller optimized file (which would shrink `FrameSize` and thus the logical footprint today) is part of the deferred proxy-lifecycle feature, not 003.
| `ParticleRenderNode` | `Graphics/Particles/ParticleRenderNode.cs:139` | hard-coded `new PixelSize(1920,1080)` → `ceil(bounds × w)`; inherit the negotiated working scale `w`; pixel-magnitude particle props × `w` | FR-029 |
| audio-visualizer drawables | `Graphics/AudioVisualizers/*` | classify pixel-magnitude params (`BarWidth`, `BlockGap`, hard-coded minimums) under FR-008 | FR-030 |
| `Scene3DRenderNode` | `Graphics3D/Scene3DRenderNode.cs` | renders at `ceil(size × s_out)` and tags the surface op `EffectiveScale.At(w)` (w == s_out), resampled at the composite boundary; internal lockstep deferred. Nested 2D scene (`SceneDrawable`) inherits the outer `s_out`/ceiling into its own `Renderer` and reports `e = At(w)` | FR-033/FR-022 |

---

## Brush / pen / text scale rules (FR-010/FR-011/FR-012)

| Type | Scaled by `s` | Invariant |
|---|---|---|
| `PerlinNoiseBrush` | `BaseFrequency` **÷ s** (period invariant) | Octaves, Seed |
| Tile/Image/Drawable brush | intermediate raster px × s | SourceRect/DestinationRect (relative) |
| `Pen` (`PenHelper`) | nothing — stroke pre-outlined in **logical** space, scaled by the root CTM (D3); cache key unchanged | Thickness/Offset/Dash effectively scale via CTM; MiterLimit/caps/joins/Trim invariant |
| `FormattedText` | **re-shaped** at `Size × s` (font size, spacing, stroke); shaping cache scale-aware | hit-test fill/stroke paths stay logical; stroke not double-CTM-scaled (D3 exception) |

---

## New public types (request/UI surface)

| Type | Purpose | Key members |
|---|---|---|
| `RenderScale` (enum/value) | FR-035 preview scale selection | `Full/Half/Quarter/FitToPreviewer`, `ToFloat(...)` |
| `EditViewModel.PreviewScale` | per-edit-view, non-persisted session state | `ReactivePropertySlim<RenderScale>`, default `Full`; not in `SaveState`/`RestoreState` |
| `EffectiveScale` (value) | FR-018 per-op supply density | `Unbounded`, `At(float)`, `Value`, `IsUnbounded` |
| `ResolutionPolicy` (value) | per-effect policy | `Inherit`/`ClampToOutput`/`Oversample(k)` (`PreserveSource` removed) |
| `FilterEffect.ResolutionPolicy` | virtual; default `Inherit` | a plugin may override; no built-in does |
| `MaxWorkingScale` | FR-037 ceiling, threaded `Renderer → RenderNodeContext` | **preview `2 × s_out`, export `+∞`** |

---

## State transitions

**Preview scale change** (FR-031/FR-035): `PreviewScale` value changes → combined `(FrameSize, PreviewScale)` observable emits → old `SceneRenderer` + `FrameCacheManager` disposed (surface, caches cleared) → new instances built on the render dispatcher → `QueueRender()` repaints. Atomic because the render lambda reads `Renderer.Value`/`FrameCacheManager.Value` fresh inside the serial dispatcher work-item.

**Export scale** (FR-034): `OutputViewModel` builds `SceneRenderer(Model, supersampleScale, disableResourceShare:true)`; `FrameProviderImpl.RenderCore` downscales `Snapshot()` to `FrameSize` when `RenderScale > 1`, asserts size == `FrameSize` before encode (FR-026). Independent of preview scale.
