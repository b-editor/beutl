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
- **Historical migration note**: the first draft attached `LosslessReRasterizable` to the now-removed `RenderNodeOperation`. `EffectiveScale.IsUnbounded` subsumes that distinction in the recorded-fragment pipeline (one concept, one member; no contradictory "lossless but `e=2.0`" state).

### Working-scale contracts (FR-036) — no `ResolutionPolicy` type
**There is no closed resolution-policy value type.** Every built-in/default filter-effect materialization starts with `MaterializeAtWorkingScale`: it runs at the **densest concrete supply, floored at `s_out`**, then is capped by the global ceiling and clamped against the concrete allocation footprint. The one standard rule (amended 2026-06-15: `s_out` floors **every standard materializing** boundary, not only the vector-only/mixed cases) is:

`RenderScaleUtilities.ResolveWorkingScale(ReadOnlySpan<EffectiveScale> inputs, float outputScale, float maxWorkingScale = +∞) → float`:
- `supply = outputScale` (the floor), then `supply = max(supply, e.Value)` over each **concrete** (non-`Unbounded`) input.
- return `min(supply, maxWorkingScale)`.
- Equivalently for `MaterializeAtWorkingScale`: `w = min( max(s_out, densest concrete supply), maxWorkingScale )`. Within this standard policy, `s_out` is the **floor**, **never** an upper ceiling — a denser concrete supply runs above it (FR-016), and a sub-output concrete supply (`At(0.5)` at a `1.0` export) is lifted to `s_out`. The former special-cased "vector-only fallback" and "C4/C5 mixed bitmap+vector floor at `s_out`" are instances of this standard floor (conclusions unchanged — they still land at `s_out`). At `s_out = 1.0` with unit-scale / vector inputs `w = max(1, 1) = 1`, so byte-identity is untouched.

- **Explicit custom filter contract**: `FilterEffectRenderNode.GetWorkingScaleContract()` returns `null` for the standard policy. An override may return `RenderScaleContract.Custom`; its resolver MUST return a finite value greater than zero and MAY intentionally return a density below `s_out`. That value is not raised to the standard floor, but it is capped by `MaxWorkingScale` and clamped against each concrete allocation footprint's 16 384-pixel axis limit. Invalid values fail instead of falling back to `s_out`. No built-in uses this hook.
- **`ResolutionPolicy` removed (and `FilterEffect.ResolutionPolicy`, `RenderNode.ResolutionPolicy`)**: earlier drafts declared a per-effect policy (`Inherit` / `ClampToOutput` / `Oversample(k)` / `PreserveSource`) to pick `w`. No closed policy type was needed, so the enum, `virtual FilterEffect.ResolutionPolicy`, `RenderNode.ResolutionPolicy` (never added — a dead duplicate), the `policy` parameter of `ResolveWorkingScale`, and the earlier `PreserveSource` floor / `preserveFloor` channel were removed. The narrow escape hatch is `FilterEffectRenderNode.GetWorkingScaleContract()` from a node returned by `FilterEffect.Resource.CreateRenderNode()`; overriding `Process` is reserved for genuinely different topology/lowering.
- **As shipped: the FR-037 ceiling IS wired** — `FilterEffectRenderNode` passes `context.MaxWorkingScale`; the editor preview seeds it at `2 × s_out` and **export seeds `+∞`** (no working-scale quality ceiling — *amended 2026-06-15*; the earlier finite `max(8, 4 × s_out)` was removed as a quality clip, see FR-037 / `WorkingScaleCeiling.Export` / `OutputViewModel`). The preview ceiling is the sole **global** upper bound on `w`. Separately, the per-buffer **dimension** clamp (FR-037(b), `RenderScaleUtilities.ClampWorkingScaleToBufferBudget`, 16384 px per axis — applied at the `FilterEffectRenderNode` node level and re-applied per target in `FilterEffectActivator.Flush` against the post-effect-inflated bounds) may further reduce `w` at an effect boundary. It is only a per-axis safeguard: aggregate byte/area/live-buffer budgeting and backend-reported limits remain out of scope, so it is not a complete OOM or allocatability guarantee. Two distinct bounds — do not conflate them (FR-037).

### Shared rounding helper (FR-007)
**Decision: no new helper type — the canonical helper *is* `PixelRect.FromRect(Rect, float scale)` / `PixelSize.FromSize(Size, float)`** (`PixelRect.cs:391`, `PixelSize.cs:209`), which already "ceil sizes (ceil'd bottom-right), toward-zero origins". The work is *adopting* the `× w` scaling at every sink with a consistent convention, not writing a new helper.
- **Invariant (byte-equality)**: origins round **toward zero** (`(int)` cast), NOT floor; extents **ceil**. **At `w = 1.0` each sink preserves its current rounding**: the main rasterization sink already uses `PixelRect.FromRect` (unchanged at scale 1); the two filter-effect sinks **keep their component-wise `(int)Width`/`(int)Height` truncation at `w = 1.0`** and apply `ceil(× w)` only for `w ≠ 1.0` — they are NOT unified with `FromRect` at scale 1 (that would change scale-1.0 output and break byte-identity). Golden-test the filter-target paths at `w = 1.0` (FR-005/FR-007).

---

## Changed core render-graph types

Feature 004 replaced the executable operation pipeline after feature 003 shipped. The active scale contract is carried by the recorded pipeline below; names from the original feature-003 implementation are retained only in the historical note at the end of this section.

### `RenderNodeContext` *(changed again by feature 004)* — `Graphics/Rendering/RenderNodeContext.cs`

`RenderNodeContext` is now the sealed, engine-created transaction recorder for one `void RenderNode.Process(RenderNodeContext)` call. `OutputScale` and `MaxWorkingScale` come from the current `RenderRequestOptions`; the context records descriptions and publishes ordered `RenderFragmentHandle` streams but never executes or owns an operation. Working-scale helpers live on the independent `RenderScaleUtilities` type so planning, brushes, 3D, and export policy use the same rule without a recorder instance.

### `RenderFragmentHandle` *(feature-004 replacement)* — `Graphics/Rendering/RenderFragmentHandle.cs`

The non-executable, non-disposable handle carries fragment cardinality, contribution, and value-input eligibility. `TryGetMetadata(out RenderFragmentMetadata)` exposes the resolved recording-time `(Bounds, EffectiveScale)` pair only when it is concrete; owning-target-dependent metadata remains symbolic until graph-wide analysis. Concrete values preserve `EffectiveScale.At(w)`, while vector/lossless values use `EffectiveScale.Unbounded`. Materialization and density reconciliation are recorded declaratively and performed later by the planner/executor.

### `RenderNodeRenderer` *(feature-004 replacement)* — `Graphics/Rendering/RenderNodeRenderer.cs`

`RenderNodeRenderer` owns repeated complete-request recording, metadata/ROI analysis, cache substitution, execution planning, target pooling, and execution. Its options seed `OutputScale`, `MaxWorkingScale`, intent, requested region, and cache policy. `Rasterize()` returns one owned `RenderNodeRasterization`; there is no public pull of executable operations.

### `RenderNodeCache` — `Graphics/Rendering/Cache/RenderNodeCache.cs`

Cache lookup and publication now occur after complete-request metadata and density demands are known. Cached values retain their concrete `EffectiveScale`; reuse is accepted only when the retained density satisfies the resolved demand, and the final blit reconciles that supply with the consuming target.

> **Historical feature-003 implementation:** the first implementation represented each result as an executable `RenderNodeOperation` and drove it through `RenderNodeProcessor`. Feature 004 removed both public surfaces rather than keeping compatibility wrappers. Any operation factories, `Pull`/`RasterizeAt`, processor-owned cache behavior, or `RenderNodeOperation.EffectiveScale` described in earlier revisions are historical implementation details, not active APIs.

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
