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
- **`s_out`** (output scale) — render-request final target only (`RenderNodeContext.OutputScale`); never upper-clamps a denser intermediate, and floors only the standard `MaterializeAtWorkingScale` policy (FR-036).
- **`e`** (effective scale) — per-op supply density (`EffectiveScale`, below).
- **`w`** (working scale) — computed for a standard buffer-allocating boundary via `ResolveWorkingScale`, or selected by an explicit custom filter scale contract (FR-036); the scale an effect runs at — device-buffer dimensions and device-space shader uniforms convert once (`× w`), logical-space geometry rides the CTM unchanged, readback geometry converts back (`÷ w`) (FR-008).

> **Glossary (naming)**: `Renderer.OutputScale` (on the renderer) `==` `RenderNodeContext.OutputScale` (on the context) `== s_out` — the same render-request output scale under two names (the context calls it `OutputScale` to stress it is *not* the working scale). The editor-facing **`RenderScale` enum** (`Full`/`Half`/`Quarter`/`FitToPreviewer`, FR-035) is a **distinct type** that *resolves to* `s_out` via `ToFloat`.

### `EffectiveScale` *(new value type)* — `Graphics/Rendering/EffectiveScale.cs`
`readonly record struct EffectiveScale` (as shipped: private `_bounded`/`_value`; `Unbounded`/`At(float)`/`IsUnbounded`/`Value`; `default == Unbounded`) — the supply density an op's pixels exist at. *(NOT a positional `(float Value, bool IsUnbounded)`: that form's `default` would wrongly be `At(0)`; see public-api.md.)*
| Member | Rule |
|---|---|
| `Unbounded` (static) | vector/lossless op — re-rasterizable at any target; **excluded from the supply `max`**. `default(EffectiveScale) == Unbounded` (byte-identity anchor: a plugin op ignoring the new param is safe). |
| `At(float scale)` (static) | a concrete bitmap density. |
- **Replaces** the first draft's separate `RenderNodeOperation.LosslessReRasterizable` bool — `IsUnbounded` subsumes it (one concept, one member; no contradictory "lossless but `e=2.0`" state).

### Working-scale contracts (FR-036) — no `ResolutionPolicy` type
**There is no closed resolution-policy value type.** Every built-in/default filter-effect materialization uses `MaterializeAtWorkingScale`: it runs at the **densest concrete supply, floored at `s_out`**, then is capped by the global ceiling and clamped against the concrete allocation footprint. The one standard rule (amended 2026-06-15: `s_out` floors **every standard materializing** boundary, not only the vector-only/mixed cases) is:

`RenderNodeContext.ResolveWorkingScale(ReadOnlySpan<EffectiveScale> inputs, float outputScale, float maxWorkingScale = +∞) → float`:
- `supply = outputScale` (the floor), then `supply = max(supply, e.Value)` over each **concrete** (non-`Unbounded`) input.
- return `min(supply, maxWorkingScale)`.
- Equivalently for `MaterializeAtWorkingScale`: `w = min( max(s_out, densest concrete supply), maxWorkingScale )`. Within this standard policy, `s_out` is the **floor**, **never** an upper ceiling — a denser concrete supply runs above it (FR-016), and a sub-output concrete supply (`At(0.5)` at a `1.0` export) is lifted to `s_out`. The former special-cased "vector-only fallback" and "C4/C5 mixed bitmap+vector floor at `s_out`" are instances of this standard floor (conclusions unchanged — they still land at `s_out`). At `s_out = 1.0` with unit-scale / vector inputs `w = max(1, 1) = 1`, so byte-identity is untouched.

- **Explicit custom filter contract**: `FilterEffectRenderNode.GetWorkingScaleContract()` returns `null` for the standard policy. An override may return `RenderScaleContract.Custom`; its resolver MUST return a finite value greater than zero and MAY intentionally return a density below `s_out`. That value is not raised to the standard floor, but it is capped by `MaxWorkingScale` and clamped against each concrete allocation footprint's 16 384-pixel axis limit. Invalid values fail instead of falling back to `s_out`.
- **`ResolutionPolicy` removed (and `FilterEffect.ResolutionPolicy`, `RenderNode.ResolutionPolicy`)**: earlier drafts declared a per-effect policy (`Inherit` / `ClampToOutput` / `Oversample(k)` / `PreserveSource`) to pick `w`. No built-in ever needed a non-default value (all are supply-driven), so the closed enum, `virtual FilterEffect.ResolutionPolicy`, `RenderNode.ResolutionPolicy` (never added — a dead duplicate), the `policy` parameter of `ResolveWorkingScale`, and the earlier `PreserveSource` floor / `preserveFloor` channel were removed. The current narrow escape hatch is `FilterEffectRenderNode.GetWorkingScaleContract()` from a node returned by `FilterEffect.Resource.CreateRenderNode()`; overriding `Process` is reserved for genuinely different topology/lowering.
- **As shipped: the FR-037 ceiling IS wired** — `FilterEffectRenderNode` passes `context.MaxWorkingScale`; the editor preview seeds it at `2 × s_out` and **export seeds `+∞`** (no working-scale quality ceiling — *amended 2026-06-15*; the earlier finite `max(8, 4 × s_out)` was removed as a quality clip, see FR-037 / `WorkingScaleCeiling.Export` / `OutputViewModel`). The preview ceiling is the sole **global** upper bound on `w`. Separately, the per-buffer **dimension** clamp (FR-037(b), `RenderNodeContext.ClampWorkingScaleToBufferBudget`, 16384 px per axis — applied at the `FilterEffectRenderNode` node level and re-applied per target in `FilterEffectActivator.Flush` against the post-effect-inflated bounds) is the sole **allocatability** bound and may further reduce `w` at an effect boundary to keep the buffer allocatable. Two distinct bounds — do not conflate them (FR-037).

### Shared rounding helper (FR-007)
**Decision: no new helper type — the canonical helper *is* `PixelRect.FromRect(Rect, float scale)` / `PixelSize.FromSize(Size, float)`** (`PixelRect.cs:391`, `PixelSize.cs:209`), which already "ceil sizes (ceil'd bottom-right), toward-zero origins". The work is *adopting* the `× w` scaling at every sink with a consistent convention, not writing a new helper.
- **Invariant (byte-equality)**: origins round **toward zero** (`(int)` cast), NOT floor; extents **ceil**. **At `w = 1.0` each sink preserves its current rounding**: the main rasterization sink already uses `PixelRect.FromRect` (unchanged at scale 1); the two filter-effect sinks **keep their component-wise `(int)Width`/`(int)Height` truncation at `w = 1.0`** and apply `ceil(× w)` only for `w ≠ 1.0` — they are NOT unified with `FromRect` at scale 1 (that would change scale-1.0 output and break byte-identity). Golden-test the filter-target paths at `w = 1.0` (FR-005/FR-007).

---

## Changed core render-graph types

### `RenderNodeContext` *(changed)* — `Graphics/Rendering/RenderNodeContext.cs`
| Member | Change | Rule |
|---|---|---|
| `OutputScale` | **+ `float OutputScale { get; }`** (get-only, default `1f`; renamed from the first draft's `Scale`) | The render-request final target `s_out` (D1). Seeded once in `RenderNodeProcessor.Pull` from `RenderNodeProcessor.OutputScale`; propagated like `IsRenderCacheEnabled`. Consumed at the root final stage, as the fallback/floor term of standard `ResolveWorkingScale`, and as explicit input available to a custom scale resolver; it does not automatically become an intermediate's working scale outside the selected contract. Get-only. |
| `ResolveWorkingScale` | **+ `static float ResolveWorkingScale(ReadOnlySpan<EffectiveScale> inputs, float outputScale, float maxWorkingScale = +∞)`** (static only — no instance overload, **no `policy` parameter**) | The standard supply-driven `MaterializeAtWorkingScale` rule (`min( max(s_out, densest concrete supply), maxWorkingScale )` — `s_out` is this policy's floor, FR-036), including the global ceiling (FR-037), which `FilterEffectRenderNode` supplies via `context.MaxWorkingScale` (preview `2 × s_out`, export `+∞`). An explicit custom contract resolves separately before the same ceiling and footprint clamp. The `policy` and `preserveFloor` parameters were removed with the `ResolutionPolicy` type. |

### `RenderNodeOperation` *(changed)* — `Graphics/Rendering/RenderNodeOperation.cs`
| Member | Change | Rule |
|---|---|---|
| `EffectiveScale` | **+ `EffectiveScale EffectiveScale { get; }`** (read-only value type, default `Unbounded`) | The supply density `e` (D1). `Unbounded` = vector/lossless (regenerate at any `w`); `At(s)` = concrete bitmap density. Set for bitmap-backed ops (`CreateFromRenderTarget`/`CreateFromSurface`, cached tiles, decoded media, 3D & nested-scene surfaces) = their `w`. |
| factory params | factories (`CreateLambda`/`CreateFromRenderTarget`/`CreateFromSurface`/`CreateDecorator`) gain `EffectiveScale effectiveScale = default` (default = `Unbounded`) | |

- **`LosslessReRasterizable` (bool) is removed** — `EffectiveScale.IsUnbounded` subsumes it (one concept, one member).
- **Relationships**: each standard materializing boundary resolves `targetScale = max(s_out, concrete input EffectiveScale)` via `RenderNodeContext.ResolveWorkingScale` (Unbounded inputs excluded); an explicit custom filter contract may select another finite positive density, including one below `s_out`. Off-target bitmap ops are Mitchell-resampled and `Unbounded` ops re-rasterized at `targetScale`, once at the `DrawSurface`/`DrawRenderTarget` blit (FR-017). Reconciliation is **distributed across the boundaries, not a central composite pass** (FR-016 clarification 2026-06-10). Upper normalization to `s_out` is **deferred to the root** (FR-016/FR-036), while either contract's concrete result remains capped/clamped before allocation.
- **Non-abstract (compat-critical)**: `EffectiveScale` is a **non-abstract** member defaulting to `Unbounded` (via the base ctor / factory param) — **never `abstract`**. The base already has three abstract members (`Bounds`/`Render`/`HitTest`, `RenderNodeOperation.cs:11-15`); an *abstract* scale member would break every in-tree subclass (the private `LambdaRenderNodeOperation`) **and every out-of-tree plugin op**. With the `Unbounded` default, a plugin op that ignores scale re-rasterizes at `w` and is byte-identical at `s_out=1.0`.
- **SaveLayer-based containers carry NO scale**: `OpacityRenderNode`/`BlendModeRenderNode`/`OpacityMaskRenderNode` are `CreateDecorator` wrappers that `PushOpacity`/`PushBlendMode`/`SaveLayer` at **render time** (`OpacityRenderNode.cs:21-28`, `ImmediateCanvas.cs:369-377`); they do **not** allocate a node-owned `RenderTarget` from `RenderNodeContext`, so they need no scale field: the `SaveLayer` captures at the current device CTM (which already carries the root `s`), and any genuinely mixed-scale child is resampled at *its own* `DrawSurface`/`DrawRenderTarget` blit inside the layer (FR-017). Only nodes that **allocate** an intermediate from `RenderNodeContext` (filter targets, brush/tile intermediates, cache tiles, nested-scene/3D surfaces) carry and consume scale.

### `RenderNodeProcessor` *(changed)* — `Graphics/Rendering/RenderNodeProcessor.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | **+ `float outputScale = 1f`**; **+ `float OutputScale { get; }`** | Seeded from `Renderer`. |
| rasterization sinks (`RasterizeAt`/`Rasterize`/`RasterizeAndConcat`) | rasterize at `w = OutputScale` (the root / cache / thumbnail sinks operate at the request's `s_out`, not a per-input negotiated scale); `PixelRect.FromRect(op.Bounds, w)`; the `ImmediateCanvas` **bakes the base CTM `CreateScale(w)`** at construction (the sink only pushes a translation-only matrix). **`w == 1` short-circuit**: a true no-op base (no scale matrix, no Save), preserving byte-identity. The standard supply-driven or explicit custom working scale (FR-036) is selected at the effect boundary (`FilterEffectRenderNode`), not here. | Identity at `w=1.0` → byte-equal. |
| `Pull` (`:167`) | `new RenderNodeContext(input, OutputScale, MaxWorkingScale)` | Single production construction site; reaches all overrides. |
| `RasterizeAt(op, w)` | **+ internal seam** generalizing `RasterizeToRenderTargets` (`:20-44`) to re-rasterize an `Unbounded` subtree at a chosen `w` | feeds FR-017 regenerate. |

### `RenderNodeCache` — `Graphics/Rendering/Cache/RenderNodeCache.cs`
**Minimally changed in 003 (multi-scale REUSE deferred — T025 `[~]`).** FR-020 scale consistency rests on a density-aware minimal fix (2026-06-11) plus manager-level invalidation: the cache helper receives the renderer's `(OutputScale, MaxWorkingScale)`, rasterizes the cache tiles at that density (forwarding the ceiling), records the creation density, and cache **replay re-tags the tiles with that density** so a downstream boundary reconciles them like any other concrete-density input — a tile is never blitted 1:1 at the wrong density. **I4 fix (2026-06-15):** because the cache rasterizes at `outputScale`, a subtree whose output carries a concrete supply density **above** `outputScale` (a transform-densified high-resolution source — `At(4)` on a 1080 timeline) would collapse that detail into the `outputScale` tile and re-tag it `At(outputScale)`, silently lowering a downstream effect's working scale once the (render-count-driven) cache kicks in — an FR-018 violation. `CreateDefaultCache` therefore **refuses to cache** any such subtree (keeping it uncached at its true supply density); the density-preserving alternative (rasterize each tile at its own working scale + a per-tile density) is the deferred T025 reuse work below. Additionally, `EditViewModel` rebuilds a fresh `SceneRenderer` **and** `FrameCacheManager` when the resolved `(FrameSize, OutputScale)` changes (`DistinctUntilChanged` + `DisposePreviousValue`; two independent UI-thread swaps — FR-031), and the per-renderer `RenderNode` cache is discarded with its renderer. The supply-aware **reuse** below was specced but **not shipped**:

> *Deferred (supply-aware reuse, defense-in-depth):* a `CachedWorkingScale` on `RenderNodeCache` + a `workingScale` param on `StoreCache` + `RenderCacheRules.Match` thresholds `÷ CachedWorkingScale²`, so an SSAA export could **reuse** a high preview cache (Mitchell-downsample) and **miss** only when it lacks detail (D6). Lands with the scale-keyed reuse work in a follow-up, not 003. (`RenderNodeCacheHelper.CreateDefaultCache` no longer builds its cache processor scale-blind as `new RenderNodeProcessor(node, false)`; it passes the renderer's `(OutputScale, MaxWorkingScale)` and the replayed tiles carry their creation density — but a tile is still never *reused across* scales.)

### `ImmediateCanvas` *(changed)* — `Graphics/ImmediateCanvas.cs`
| Member | Change | Rule |
|---|---|---|
| `DrawSurface`/`DrawRenderTarget` (`:106-126`) | **+ `(src, dest, SKSamplingOptions)` resample path** via `Canvas.DrawImage(...,Mitchell)` for the FR-017 mixed-scale blit. **Branch on exact `srcScale == destScale` → today's bare 1:1 `Canvas.DrawSurface(...,paint)`** (byte-identity); only the `≠` case routes through the resampler. | FR-017; byte-identity-critical short-circuit. |

---

## Changed renderer / request types

### `IRenderer` *(changed)* — `Graphics/Rendering/IRenderer.cs`
| Member | Change | Rule |
|---|---|---|
| `OutputScale` | **+ `float OutputScale { get; }`** (default-interface-impl → `1f` to soften third-party impls, mirroring the `GetBoundary` default at `:30`) | |
| `DeviceSize` | **+ `PixelSize DeviceSize { get; }`** = `ceil(FrameSize × OutputScale)` | FR-003/FR-026. |

### `Renderer` *(changed)* — `Graphics/Rendering/Renderer.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | `(int width, int height)` → **`(int width, int height, float renderScale = 1f)`** | width/height stay **logical** FrameSize; device surface = `ceil(FrameSize × renderScale)`. BREAKING (FR-028). |
| `OutputScale` | **+ `float OutputScale { get; }`** | Immutable per instance (D4). |
| `FrameSize` | unchanged (logical) | |
| `Render`/`RenderDrawable` | the root `ImmediateCanvas` bakes the base CTM `CreateScale(renderScale)` at construction (no per-frame push); the FPS overlay re-enters device space via `FpsDrawer.Dispose` → `PushDeviceSpace` so it stays unscaled | |
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
| `DrawBackdrop` (`:366`) | `new Rect(canvasSize)` — `canvasSize` is now an exact logical `Size` (no `.ToSize(1)`); the snapshot records its capture scale (`ImmediateCanvas.Snapshot` → `TmpBackdrop`) and the replay un-scales by *that*, so the node bounds stay logical and `outputScale` is **not** applied here | FR-021 scale-aware backdrop (capture-scale model). |

---

## Changed filter-effect types

### `FilterEffectContext` *(changed)* — `Graphics/FilterEffects/FilterEffectContext.cs`
| Member | Change | Rule |
|---|---|---|
| ctor | **+ `float outputScale, float workingScale`** | `workingScale` = the negotiated `w` from `FilterEffectRenderNode`'s standard or explicit custom contract; `outputScale` = `s_out`. |
| `WorkingScale` | **+ `float WorkingScale { get; }`** | FR-015 read accessor — the `w` the effect runs at. |
| `OutputScale` | **+ `float OutputScale { get; }`** | the eventual delivery target, for effects that need it. |
| Skia `SKImageFilter` primitives (Blur/DropShadow/Dilate/Erode/MatrixConvolution/Transform) | **NOT** multiplied by `WorkingScale` — they ride the `CreateScale(w)` CTM in `FilterEffectActivator.Flush`, so Skia scales their params for free; multiplying here would double-scale. Only **CustomEffect point-blit** code (InnerShadow, Mosaic, ColorShift, …) multiplies absolute-length args by `WorkingScale` | FR-009. |

### `CustomFilterEffectContext` *(changed)* — `Graphics/FilterEffects/CustomFilterEffectContext.cs`
| Member | Change | Rule |
|---|---|---|
| `WorkingScale` | **+ `float WorkingScale { get; }`** (renamed from `RenderScale`) | FR-015 accessor for custom/shader effects. |
| `CreateTarget(Rect)` | size `ceil(bounds × WorkingScale)` for `w ≠ 1.0`, keeping component-wise `(int)` at `w = 1.0` (byte-identity); `Open` returns a canvas with the **baked base CTM `CreateScale(density)`** where `density = target.Scale.Value`, or `WorkingScale` when the target is `Unbounded` (e.g. a plugin-built target with no Scale set); the author draws logical content directly (no manual prescale) | FR-009/FR-007. |

### `FilterEffectActivator` *(changed)* — `Graphics/FilterEffects/FilterEffectActivator.cs`
`Flush` (`:23`) sizes targets `ceil(OriginalBounds × w)` for `w ≠ 1.0`, **keeping the current component-wise `(int)Width`/`(int)Height` truncation at `w = 1.0`** (byte-identity); the flatten `ImmediateCanvas` **bakes the base CTM `CreateScale(w)`** (the flush pushes a translation-only matrix) and tags each flushed buffer `EffectiveScale.At(w)`. `w`, `s_out` **and `maxWorkingScale`** are supplied to the ctor (from `FilterEffectRenderNode` after its standard or explicit custom contract and allocation-footprint clamp), not derived from the targets, and exposed as `OutputScale` / `MaxWorkingScale` getters forwarded into the nested `FilterEffectContext`/`CustomFilterEffectContext` (so nested pulls stay under the request's FR-037 ceiling). Scale-1.0-sensitive (golden-tested).

### `EffectTarget` *(changed)* — `Graphics/FilterEffects/EffectTarget.cs`
| Member | Change | Rule |
|---|---|---|
| `Scale` | **+ `EffectiveScale Scale { get; set; }`** (default `Unbounded`) | Per-intermediate supply density, set from the producing op's `e`, so divergent-scale inputs normalize to `w` before a shared filter/flatten (FR-019; LayerEffect/DelayAnimation/InnerShadow/Blend/Mosaic). Propagated through `Clone`/flush re-wrap. |
| `Empty`/`Size` | **removed** (obsolete) | Per AGENTS.md no-shim policy. |

`EffectTargets`: no scale accessor — `w` is selected once by `FilterEffectRenderNode` through the standard or explicit custom contract and threaded through the activator, so the targets do not derive it. (Earlier drafts' `MaxScale()`/`ResolveScale(...)` were both dropped.) `CalculateBounds` (`:27`) stays logical (scale-invariant).

---

## Changed media / drawable types

| Type | File | Change | Rule |
|---|---|---|---|
| `SourceImage` | `Graphics/SourceImage.cs:26` | `Source.FrameSize.ToSize(1)` → logical size decoupled from decoded pixel size; node Bounds logical | FR-023 |
| `SourceVideo` | `Graphics/SourceVideo.cs:139` | same | FR-023 |
| `Image/VideoSourceRenderNode` | `Graphics/Rendering/*` | draw at native pixel extent under the active CTM; tag `EffectiveScale.At(1)` (logical == decoded `FrameSize` in 003, so the ratio is exactly 1). A per-frame `decodedPixels / logicalSize` density arrives with proxy decode (scope note line 169). | FR-024 |
| `MediaOptions` | `Media/Decoding/MediaOptions.cs` | **unchanged in 003**; kept additively extensible for a future decode-scale hint | FR-025 |

> **003 scope note (logical vs decoded size)**: `ImageSource.Resource.FrameSize` today = the **decoded pixel size** (`new PixelSize(counter.Width, counter.Height)`, `ImageSource.cs:93`); likewise `VideoSource`. Because proxy decode is out of scope (FR-025), in 003 a source's **logical size == its full decoded `FrameSize`** — no separate intrinsic-logical-size channel. FR-023/FR-024 establish only the *seam*: a source draws into a `logicalSize × s` destination rect (not a native-px 1:1 blit), so a **future** reduced-decode supply can shrink the decoded bitmap while the logical footprint stays fixed. Pointing a source directly at a smaller optimized file (which shrinks `FrameSize` and thus the logical footprint today) is part of the deferred proxy-lifecycle feature, not 003.
| `ParticleRenderNode` | `Graphics/Particles/ParticleRenderNode.cs:139` | hard-coded `new PixelSize(1920,1080)` → `ceil(bounds × w)`; inherit the negotiated working scale `w`; pixel-magnitude particle props × `w` | FR-029 |
| audio-visualizer drawables | `Graphics/AudioVisualizers/*` | classify pixel-magnitude params (`BarWidth`, `BlockGap`, hard-coded minimums) under FR-008 | FR-030 |
| `Scene3DRenderNode` | `Graphics3D/Scene3DRenderNode.cs` | renders at `ceil(size × s_out)` and tags the surface op `EffectiveScale.At(w)` (w == s_out), resampled at the composite boundary; internal lockstep deferred. Nested 2D scene (`SceneDrawable`) inherits the outer `s_out`/ceiling into its own `Renderer` and reports `e = At(w)` | FR-033/FR-022 |
| `TextureSource` / `DrawableTextureSource` *(added 2026-06-15)* | `Graphics3D/Textures/{TextureSource,DrawableTextureSource}.cs` | `TextureSource.Resource.GetTexture` gains an additive `float surfaceDensity = 1f` (mirroring `IRenderer3D.SurfaceDensity` / `RenderContext3D.SurfaceDensity`). `DrawableTextureSource` rasterizes its re-rasterizable `Drawable` at `ceil(authorSize × surfaceDensity)` (clamped by `ClampWorkingScaleToBufferBudget`) so a vector label/logo stays crisp on a supersampled / high-density 3D surface instead of being frozen at the authored pixel count and GPU-magnified. A decoded-bitmap source ignores `surfaceDensity` (its pixels are fixed). Default `1f` keeps existing impls source-compatible and byte-identical at `surfaceDensity == 1`. | FR-033/FR-022 |

---

## Brush / pen / text scale rules (FR-010/FR-011/FR-012)

| Type | Scaled by `s` | Invariant |
|---|---|---|
| `PerlinNoiseBrush` | **unchanged** — `BaseFrequency` rides the CTM (period logical-invariant); the earlier "÷ s" rule was dropped (empirically worse at reduced scale, FR-010); best-effort (FR-013) | Octaves, Seed, BaseFrequency |
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
| `MaxWorkingScale` | FR-037 ceiling, threaded `Renderer → RenderNodeContext` | **preview `2 × s_out`, export `+∞`** (no export quality ceiling — *amended 2026-06-15*) |

*(No `ResolutionPolicy` type and no `FilterEffect.ResolutionPolicy` — removed. The default working scale is supply-driven through `MaterializeAtWorkingScale`; an effect that needs a different one overrides `GetWorkingScaleContract()` in a custom `FilterEffectRenderNode`, where an explicit `RenderScaleContract.Custom` may choose a finite positive density below `s_out` before the common ceiling and footprint clamp. `Process` remains the escape hatch for genuinely different topology/lowering. See FR-036.)*

---

## State transitions

**Preview scale change** (FR-031/FR-035): `PreviewScale` value changes → resolved `(FrameSize, OutputScale)` observable emits (`DistinctUntilChanged`) → old `SceneRenderer` + `FrameCacheManager` disposed (`DisposePreviousValue`: surface, caches cleared) → new instances built (on the **UI thread** inside the reactive `Select`, NOT on the render dispatcher) → `QueueRender()` repaints. The render lambda reads `Renderer.Value`/`FrameCacheManager.Value` fresh inside the serial render-dispatcher work-item, so a single composite never tears; each renderer is immutable per instance. The swap itself is two independent reactive-property updates on the UI thread (NOT a single atomic snapshot) — see the FR-031 clarification (2026-06-10) for the narrow, self-healing window and the fully-atomic-snapshot follow-up.

**Export scale** (FR-034): `OutputViewModel` builds `SceneRenderer(Model, supersampleScale, disableResourceShare:true)`; `FrameProviderImpl.RenderCore` downscales `Snapshot()` to `FrameSize` when `OutputScale > 1`, asserts size == `FrameSize` before encode (FR-026). Independent of preview scale.
