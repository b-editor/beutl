# Data Model: Per-Clip Proxy via RenderNodeOperation CorrectionScale

This document enumerates the new types, modified types, and per-RenderNode-subclass behaviour introduced by this feature.

> **Full rewrite (2026-05-22)**: Earlier drafts of this file enumerated wrapper structs (`PixelLength` / `PixelExtent` / `PixelOffset`), animator registrations, property-editor changes, helper-internal scaling on `FilterEffectContext` / `GraphicsContext2D`, and per-effect property migrations — all built on the scene-wide-proxy assumption that was abandoned. Under the per-clip proxy design (see `spec.md` § Clarifications 2026-05-22 and `research.md`), most of those types are no longer needed. The current data model is much smaller.

## New types

### `RenderScale` — `Beutl.Graphics.Rendering.RenderScale`

A small value type carrying a 2D scale ratio:

```csharp
public readonly struct RenderScale : IEquatable<RenderScale>
{
    public RenderScale(float scaleX, float scaleY);
    public float ScaleX { get; }   // bounds.Width / raster.Width  (always ≥ 1; = 1 means no proxy)
    public float ScaleY { get; }   // bounds.Height / raster.Height

    public static RenderScale Identity { get; }                    // (1, 1)
    public static RenderScale FromRatio(float ratio);              // uniform (ratio, ratio); ratio ≥ 1 for proxy
    public static RenderScale FromFrames(PixelSize raster, PixelSize bounds);
        // Per-axis: bounds / raster. Validates ScaleX ≥ 1 and ScaleY ≥ 1.

    // Authoring-space length → raster-space (smaller). Used by transformers.
    public float ToRasterX(float lengthAuthoring);                 // = lengthAuthoring / ScaleX
    public float ToRasterY(float lengthAuthoring);                 // = lengthAuthoring / ScaleY
    public float ToRasterUniform(float lengthAuthoring);
    public Size  ToRaster(Size sizeAuthoring);
    public Point ToRaster(Point pointAuthoring);

    // Raster-space length → authoring-space (larger). Symmetric helper.
    public float ToAuthoringX(float lengthRaster);                 // = lengthRaster * ScaleX
    public float ToAuthoringY(float lengthRaster);
}
```

**Numeric convention (fixed and authoritative)**: `CorrectionScale = bounds.Size / raster.Size` per axis. `CorrectionScale = (4, 4)` means "the upstream produced a raster 1/4 the linear size of its bounds; the compositor upscales 4× when blitting". `Identity = (1, 1)` means "no proxy; raster matches bounds 1:1".

- **Validation**:
  - Constructor: `ScaleX ≥ 1`, `ScaleY ≥ 1`, both finite. Throws `ArgumentOutOfRangeException` otherwise. Values < 1 would mean "raster is larger than bounds" — that is not what `CorrectionScale` represents and is rejected.
  - `FromFrames(raster, bounds)`: requires `raster.PixelSize > 0`, `bounds.PixelSize > 0`, `raster.Width ≤ bounds.Width`, `raster.Height ≤ bounds.Height`. Throws `ArgumentException` otherwise.
  - Non-uniformity (`|ScaleX − ScaleY| > 1e-3 * max(ScaleX, ScaleY)`) is allowed by the type — per-clip proxy may have minor non-uniformity due to integer rounding. Source nodes that intentionally produce non-square-pixel proxies are responsible for that decision.
- **`Identity`**: `(1, 1)`; the default reported by every `RenderNodeOperation.CorrectionScale` until proxy is enabled.

### `RenderNodeOperation.CorrectionScale` (new virtual property)

```csharp
public abstract partial class RenderNodeOperation : IDisposable
{
    public abstract Rect Bounds { get; }

    // NEW
    public virtual RenderScale CorrectionScale => RenderScale.Identity;

    public abstract void Render(ImmediateCanvas canvas);
    public abstract bool HitTest(Point point);
    // … existing members …
}
```

Default = `Identity`. Source-producing RenderNode subclasses override (via stored field on the concrete operation type) when they apply proxy. See `contracts/render-node-operation-scale.md`.

### Factory overloads on `RenderNodeOperation`

```csharp
public abstract partial class RenderNodeOperation : IDisposable
{
    public static RenderNodeOperation CreateLambda(
        Rect bounds, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null, Action? onDispose = null,
        RenderScale correctionScale = default);   // default = Identity

    public static RenderNodeOperation CreateFromRenderTarget(
        Rect bounds, Point position, RenderTarget renderTarget,
        RenderScale correctionScale = default);

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, SKSurface surface,
        RenderScale correctionScale = default);

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, Ref<SKSurface> surface,
        RenderScale correctionScale = default);

    public static RenderNodeOperation CreateDecorator(
        RenderNodeOperation child, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null, Action? onDispose = null);
        // CreateDecorator inherits CorrectionScale from child.
}
```

The `default(RenderScale)` is **not** `Identity` — it's `(0, 0)`, which is invalid. To preserve back-compat without forcing callers to specify, we either (a) provide both old and new factory overloads, or (b) normalize `(0, 0)` to `Identity` inside the factory. Decision: **option (b)** — the factory inspects the value and substitutes `Identity` if it equals `default(RenderScale)`. This keeps the existing call sites byte-identical while letting new callers pass an explicit `CorrectionScale`.

## Modified types

### `RenderNodeOperation`

Adds `CorrectionScale` virtual property (default `Identity`) and the new factory overloads above. Concrete subclasses (the `LambdaRenderNodeOperation` private class inside `RenderNodeOperation`) gain a stored `_correctionScale` field and override the virtual.

### `RenderTarget`-emitting source nodes

`VideoSourceRenderNode`, `ImageSourceRenderNode`, and `DrawableRenderNode` (when rendering a sub-canvas like a nested Scene) gain logic to:

1. Decide their raster size based on per-clip proxy configuration (out of scope for this PR; defaults to "no proxy").
2. Construct their inner `ImmediateCanvas` (Type B sources) with the appropriate `SKCanvas.Scale(1/CorrectionScale)` so that the inner render pass operates in authoring space.
3. Set `CorrectionScale` on the produced operation.

See `contracts/source-node-proxy.md`.

### Transformer RenderNode subclasses

`FilterEffectRenderNode`, `TransformRenderNode`, `ContainerRenderNode`, and push-state nodes (`ClipRenderNode`, `LayerRenderNode`, `OpacityMaskRenderNode`) gain logic to:

1. Read upstream `CorrectionScale`.
2. Divide length-typed internal parameters by it before invoking Skia.
3. Compute output `Bounds` in authoring space using the **authored** (un-divided) parameters.
4. Propagate `CorrectionScale` to the output operation.

See `contracts/transformer-node-scale-handling.md`.

### `ImmediateCanvas` (extension)

Add `DrawRenderTarget` / `DrawSurface` variants (or a new helper `DrawScaled(...)`) that accept a `CorrectionScale` and apply the upscale transform during the blit. Used by the compositor; see `contracts/compositor-blit.md`.

## T001 audit — RenderNode subclasses under `src/Beutl.Engine/Graphics/Rendering/`

End-to-end walk of every `RenderNode` subclass actually present in the tree, with the proxy-handling category each one falls into. **Categories**:

- **Source A (media-decoding)**: the node's operation contains an already-rasterized pixel array (decoded frame, image bitmap). Per-clip proxy means decoding at proxy resolution and shipping the smaller raster.
- **Source B (sub-canvas)**: the node allocates a fresh raster (e.g. `RenderTarget`), constructs an inner `ImmediateCanvas`, runs an inner render pass, and ships the result. Per-clip proxy means allocating the raster at proxy resolution with `SKCanvas.Scale(1/CorrectionScale)` pre-applied so the inner pass renders in authoring space.
- **Source direct-draw**: the node's operation simply records a `DrawXxx(...)` call against whatever surrounding `ImmediateCanvas` the compositor passes in. It does not pre-rasterize. `CorrectionScale = Identity` from this node's own perspective; per-clip proxy participation happens implicitly via the surrounding Source-B canvas's `SKCanvas.Scale` matrix.
- **Transformer**: the node consumes upstream operations, applies SkImageFilter / matrix / clip / push-state, and emits a new operation. Reads upstream `CorrectionScale`, divides length-typed parameters before invoking Skia, computes output `Bounds` in authoring space, propagates `CorrectionScale` unchanged (unless it materializes a fresh raster, in which case it becomes Source-B-like for downstream).
- **Passthrough**: the node forwards upstream operations unchanged.
- **N/A**: debug / inert nodes — no proxy participation needed.

| File | Category | Phase-3 responsibility |
|---|---|---|
| `VideoSourceRenderNode.cs` | Source A | Today it records a `Lambda` that calls `canvas.DrawBitmap` against the parent canvas. Phase-3 wiring: when proxy is enabled, decode at proxy resolution and either rasterize to a `RenderTarget` (then ship via `CreateFromRenderTarget(..., correctionScale)`) or keep the draw-into-canvas op but declare the appropriate `CorrectionScale`. Default `Identity`. |
| `ImageSourceRenderNode.cs` | Source A | Same pattern as Video. Static images default to `Identity` (proxy rarely useful) but the mechanism is wired. |
| `DrawableRenderNode.cs` | Source B (Container-derived; renders a `Drawable` sub-tree) | **Phase-3 task T010 skipped — deferred to per-clip proxy follow-up feature.** Currently a pass-through (`Process` returns `context.Input` via `ContainerRenderNode`). Type B sub-canvas materialization (allocate inner raster at `bounds.PixelSize / scale`, push `SKCanvas.Scale(1/scale)`, declare `CorrectionScale = scale`) is the original design but depends on the undefined `ProxyConfig` schema (out of scope here) and a `SceneDrawable` audit. Will be picked up with the follow-up feature. |
| `RectangleRenderNode.cs` | Source direct-draw | No change. `CreateLambda(..., canvas => canvas.DrawRectangle(...))` participates via the surrounding canvas matrix. |
| `EllipseRenderNode.cs` | Source direct-draw | No change. |
| `GeometryRenderNode.cs` | Source direct-draw | No change. |
| `TextRenderNode.cs` | Source direct-draw | No change. |
| `ClearRenderNode.cs` | Source direct-draw | No change. Clears the surrounding canvas. |
| `DrawBackdropRenderNode.cs` | Source direct-draw | No change. Backdrop bitmap drawn into surrounding canvas. |
| `SnapshotBackdropRenderNode.cs` | Source B (snapshot path) | If the surrounding canvas is at proxy scale, the snapshot is at proxy resolution; declares `CorrectionScale` matching the surrounding render. Default `Identity` until snapshot path participates. |
| `BrushRenderNode.cs` (abstract) | n/a — base for direct-draw shapes | No change. |
| `FilterEffectRenderNode.cs` | Transformer | Phase-3: read upstream `CorrectionScale`; per-effect parameter division (see `research.md` § R3); output `Bounds` in authoring space; propagate `CorrectionScale`. |
| `TransformRenderNode.cs` | Transformer (matrix) | Phase-3: bounds transform in authoring space; propagate `CorrectionScale`. Matrix translation column stays in authoring units. |
| `ContainerRenderNode.cs` | Transformer (aggregator) | Phase-3: each child's `CorrectionScale` flows through independently. Mixed-scale composition is supported by the compositor at blit time. |
| `BlendModeRenderNode.cs` | Transformer (push-state — blend mode) | Phase-3: propagate. |
| `OpacityRenderNode.cs` | Transformer (push-state — alpha) | Phase-3: propagate. |
| `OpacityMaskRenderNode.cs` | Transformer (push-state — mask) | Phase-3: mask bounds in authoring space; propagate. The mask brush's own internal matrices compose through the surrounding canvas scale. |
| `RectClipRenderNode.cs` | Transformer (push-state — rect clip) | Phase-3: clip rect in authoring space; propagate. |
| `GeometryClipRenderNode.cs` | Transformer (push-state — geometry clip) | Phase-3: clip path in authoring space; propagate. |
| `LayerRenderNode.cs` | Transformer (push-state — saveLayer) | Phase-3: if `saveLayer` materializes a new raster, the new raster is at the surrounding canvas's scale → behaves Source-B-like for downstream. Default propagate. |
| `PushRenderNode.cs` | Transformer (push-state — generic Push) | Phase-3: propagate. |
| `OperationWrapperRenderNode.cs` | Passthrough | No change. Wraps pre-built operations; their `CorrectionScale` is reported unchanged. |
| `ReferencesChildRenderNode.cs` | Passthrough | No change. |
| `MemoryNode.cs` (generic) | N/A (no operations) | No change. |
| `FpsText.cs` (internal) | N/A (debug overlay drawn at compositor scale) | No change. |
| `RenderNode.cs` (abstract base) | n/a | Base class; no override. |
| `RenderNodeOperation.cs` | n/a (the carrier) | This file gets `RenderScale CorrectionScale` virtual + factory-method overloads (Phase 2 — Block A). |
| `RenderNodeContext.cs` | n/a | No change. |
| `RenderNodeProcessor.cs` | n/a (orchestrator) | Phase-3: blit path consumes `CorrectionScale`. |
| `Renderer.cs` | Compositor | Phase-3: compositor walks operations and pushes `SKCanvas.Scale(scaleX, scaleY, bounds.X, bounds.Y)` per non-Identity op before calling `Render`. |
| `ImmediateCanvas.cs` (in `Graphics/`, used here) | Compositor blit helper | Phase-3: extended to accept the upscale during `DrawRenderTarget` / `DrawSurface`. |

**No unclassified subclass**: every `RenderNode`-derived class in `src/Beutl.Engine/Graphics/Rendering/` was inspected and falls into one of the categories above. No follow-up is required from the audit.

## Implementation deviation (2026-05-22, Phase 3 session)

The original "no FilterEffectContext modification" constraint did not survive contact with the actual code. `FilterEffectContext` builds `SKImageFilter` through closed factory lambdas embedded in `FEItem_Skia<T>` items, and `FilterEffectRenderNode` invokes them via `FilterEffectActivator`. There is no clean injection point that divides length-typed parameters from outside the effect-helper chain. The user's directive on this session resolved the gap:

> **"FilterEffectContext で実装されるプリミティブなエフェクトは FilterEffectContext でスケールを適用し、それ以外の CustomEffect は FilterEffect でスケールしてください"**

Adopted policy (Phase 3 implementation):

- `FilterEffectContext` **gains** `RenderScale CorrectionScale { get; internal set; } = RenderScale.Identity`. Its length-typed primitives (`Blur`, `DropShadow`, `DropShadowOnly`, `InnerShadow{Core,Only}`, `Transform`, `Erode`, `Dilate`) divide their length-typed authored parameters by `CorrectionScale` at call site before storing into `FEItem_Skia<T>`. The data tuple embeds *both* the raster-divided params (consumed by the Skia factory) and the authored params (consumed by `transformBounds`) so output bounds stay in authoring space.
- `CustomFilterEffectContext` **gains** a public `RenderScale CorrectionScale { get; }`. CustomEffect-based effects (effects whose `ApplyTo` calls `context.CustomEffect(...)` to build their own filter / shader) **must read this value and divide their own length-typed authored parameters** before invoking Skia. Among the 13 in-scope effects, this applies to: `StrokeEffect`, `ColorShift`, `DisplacementMapTransform`, `FlatShadow`, `Clipping`, `SplitEffect`, `ShakeEffect`, `MosaicEffect`. Those source files must be updated in a follow-up pass (out of scope for this session). Pure-primitive in-scope effects (`Blur`, `DropShadow`, `InnerShadow`, `Erode`, `Dilate`) participate **automatically** through `FilterEffectContext` and need **no source change**.
- `FilterEffectRenderNode.Process` reads `ComponentWiseMax` of upstream `CorrectionScale` (Pattern Y from `contracts/transformer-node-scale-handling.md`), sets `feContext.CorrectionScale` to that value, and propagates the same `CorrectionScale` on output operations.
- `MatrixConvolution` (power-user, not in the 13 in-scope effects) is **deferred**.

This loosens the original "zero FilterEffectContext modification" constraint but preserves the spirit of `FR-008` for the 5 pure-primitive in-scope effects. The 8 CustomEffect-based effects opt in via a single property read.

### Per-effect status (Phase 4 cleanup, 2026-05-22)

| Effect | Type | Length-typed parameter handling | Visual correctness at non-Identity upstream |
|---|---|---|---|
| Blur | Primitive | Divided in `FilterEffectContext.Blur` | ✓ |
| DropShadow | Primitive | Divided in `FilterEffectContext.DropShadow` | ✓ |
| InnerShadow | Primitive (via `InnerShadowCore`) | Divided in `FilterEffectContext.InnerShadowCore` | ✓ |
| Erode | Primitive | Divided in `FilterEffectContext.Erode` | ✓ |
| Dilate | Primitive | Divided in `FilterEffectContext.Dilate` | ✓ |
| MosaicEffect | CustomEffect (SKSL shader) | `TileSize` uniform divided in effect's action; ApplyToNewTarget now draws at physical extent | ✓ |
| ColorShift | CustomEffect (SKSL shader) | All 4 offset uniforms divided in effect's action | ✓ |
| ShakeEffect | CustomEffect (authoring-bounds translation) | None — translates bounds in authoring space, CorrectionScale-agnostic by design | ✓ |
| FlatShadow | CustomEffect (contour trace + pixel draws) | TODO marker placed; needs raster-coord remediation in the contour-drawing inner loop. | Pending |
| Clipping | CustomEffect (pixel-extent edit + DrawRenderTarget) | DrawRenderTarget offsets and inner translate divided by CorrectionScale | ✓ |
| SplitEffect | CustomEffect (multi-RT split via DrawRenderTarget at authoring offsets) | Per-cell DrawRenderTarget offsets divided by CorrectionScale | ✓ |
| StrokeEffect | CustomEffect (contour trace + DrawRenderTarget) | Not modified. Needs raster-coord remediation in the contour-trace path (same shape as FlatShadow). | Pending |
| DisplacementMapTranslateTransform | CustomEffect (SKSL shader) | `uTranslation` divided in effect's action | ✓ |
| DisplacementMapScaleTransform | CustomEffect (SKSL shader, dimensionless ratio) | `uPivot` divided in effect's action; `uScale` stays dimensionless | ✓ |
| DisplacementMapRotationTransform | CustomEffect (SKSL shader, dimensionless angle) | `uPivot` divided in effect's action; `uAngle` stays dimensionless | ✓ |

11 of 13 in-scope effects now respond correctly to non-Identity upstream `CorrectionScale`. The 2 remaining (FlatShadow and StrokeEffect) both follow the "snapshot upstream → trace contour → replay via `DrawPath`" pattern. Their contour points come back in raster pixel coords (from `ContourTracer.FindContours` against the upstream snapshot) and are drawn into the new `EffectTarget` whose canvas operates in physical raster units. To make them visually correct at non-Identity upstream, the contour-drawing loop needs to:

1. Multiply contour pixel coords by `CorrectionScale` before drawing (lifting them into authoring units), and divide the per-iteration unit offset by `CorrectionScale` (so the `length` iterations advance by 1 raster pixel each).
2. **Or** keep contour coords in raster pixels and divide outer translation offsets by `CorrectionScale` instead.

Either approach is local to the effect's `Apply` action and does not require further engine plumbing. Left as a small follow-up.

### Structural plumbing (Phase 4 cleanup, 2026-05-22)

The structural support these effects rely on is now in place:

- `EffectTarget.CorrectionScale` — every EffectTarget carries the raster-vs-authoring ratio. Constructed-from-NodeOperation EffectTargets pick it up from the wrapped operation; constructed-from-RenderTarget EffectTargets take it as a constructor parameter.
- `CustomFilterEffectContext.CreateTarget(bounds)` allocates the new `RenderTarget` at `bounds.PixelSize / CorrectionScale` (rounded up, minimum 1 pixel per axis) and returns an EffectTarget carrying that scale. At Identity it stays byte-equivalent to the pre-feature allocation.
- `FilterEffectActivator.Flush` materializes upstream targets at the upstream raster scale (so the chain stays at proxy resolution end-to-end instead of upscaling on the first primitive-driven flush).
- `SKSLShader.ApplyToNewTarget` draws across the new RT's physical extent (`bounds.W / scale.X × bounds.H / scale.Y`) so the shader's `coord` runs over every physical pixel of the upstream-scale raster.
- `CustomFilterEffectContext.Open(target)` returns the canvas unchanged — actions are expected to operate in physical raster units. The natural mental model is "the EffectTarget is already at upstream scale, so authored-pixel offsets need to be divided by `CorrectionScale` before being passed to `DrawRenderTarget` / `PushTransform`". A `Scale(1/scale)` matrix push on `Open` was tried and rejected because it interacts badly with `DrawRenderTarget`'s pixel-size semantics (the source image's pixel dimensions are treated as logical units and get further shrunk by the matrix).

## NOT modified

These types are still **left unchanged** by this PR:

- `GraphicsContext2D` — no scaled helpers, no `*Raw` twins, no `PushReferenceFrame`. `DrawRectangle(Rect)` etc. record verbatim.
- `IRenderer` — no `ReferenceFrame` property.
- `Renderer` — no new constructor overload taking `referenceFrame`. Constructor signature unchanged.
- `Pen.cs`, `PenHelper.cs` — no `GetScaledThickness` / `GetScaledBounds` family.
- `Transform` subclasses (`TranslateTransform`, `Rotation3DTransform`, `MatrixTransform`, …) — no source changes, no scaling in `CreateMatrix`.
- `CompositionContext` — no `RenderScale` property.
- The 5 pure-primitive in-scope `FilterEffect` subclasses (Blur, DropShadow, InnerShadow, Erode, Dilate) — no source changes (FR-008 preserved for these).
- All `Drawable` / `Shape` subclasses — no source changes.
- `Property` system, animators, property editors — no source changes.

## In-scope effects (the 13 from T001 audit) — descriptive, no source change

The 13 in-scope effects benefit automatically because **the `FilterEffectRenderNode` that materializes each effect at render time** handles the upstream `CorrectionScale`. The effect class itself (e.g. `Blur.cs`) is untouched.

| Effect class | Where the scaling happens | Source modification |
|---|---|---|
| `Blur` | `FilterEffectRenderNode` for Blur reads upstream CorrectionScale and divides `Sigma.Width / .Height` before invoking `SKImageFilter.CreateBlur` | None |
| `DropShadow` | Same RenderNode pattern, divides `Position` and `Sigma` per axis | None |
| `InnerShadow` | Same | None |
| `StrokeEffect` | Same; offset divided per axis; Pen.Thickness reaches Skia via the surrounding canvas's matrix (handled by the surrounding source's SKCanvas.Scale) | None |
| `Erode`, `Dilate` | Same; radius divided per axis | None |
| `FlatShadow` | Same; `Length / ScaleUniform` | None |
| `ColorShift` | Same; per-channel offsets divided | None |
| `DisplacementMapTransform` (3 subclasses) | Same; `X / Y / CenterX / CenterY / Depth` divided | None |
| `MosaicEffect` | Same; tile size divided per axis | None |
| `ShakeEffect` | Same; `StrengthX / StrengthY` divided | None |
| `SplitEffect` | Same; spacing divided per axis | None |
| `Clipping` | Same; edges divided per axis | None |
| Out of scope (dimensionless effects: `Brightness`, `Saturate`, etc.) | n/a — no length parameters | None |

Per-effect handling lives in the `FilterEffectRenderNode` (or whatever concrete render-node type each effect produces). The `tasks.md` Block C iterates the effects and confirms each.

## Geometry / TextBlock / Brush — now in scope

These were "deferred follow-ups" in prior drafts. Under the per-clip proxy design, they participate automatically:

- **Shapes** (`RectShape`, `EllipseShape`, `RoundedRectShape`): rendered via `RectShape.Render → context.DrawRectangle`. The `DrawRectangle` call records into the surrounding canvas, whose `SKCanvas.Scale` matrix (set by the enclosing source's renderer) handles the per-source scaling.
- **`TextBlock.Size / Spacing`**: rendered via `DrawText` into the surrounding canvas. Font size and glyph metrics are in authoring units; `SKCanvas.Scale` matrix transforms them. Works out automatically when the surrounding canvas has the right matrix.
- **`Geometry` path coordinates**: passed to `SKCanvas.DrawPath`; transformed by current canvas matrix.
- **`Brush` internal rectangles** (`TileBrush`, `ImageBrush.SourceRect / DestinationRect`): brushes apply via `SKShader` with a local matrix; the surrounding canvas matrix composes through.

The single insight that makes all of this work: **Skia's matrix transformation is composed into every length-typed Skia call**. Pre-multiply the SKCanvas matrix with the scale (which is what Type B sources do per `contracts/source-node-proxy.md`), and every downstream operation participates without code changes.

## Serialization

**Project files do not change shape.** No property type is renamed; no new property is added to existing classes; the only schema change (when the follow-up feature lands) is the addition of per-clip proxy settings, which is out of scope here.

FR-005 ("no project-file migration step") is satisfied trivially: there are no engine-side serialization changes in this PR.

## Validation summary

| Rule | Enforced where |
|---|---|
| `RenderScale.ScaleX ≥ 1`, `ScaleY ≥ 1`, both finite | `RenderScale` constructor |
| `FromFrames(raster, bounds)` validates `raster ≤ bounds` per axis and both > 0 | `FromFrames` factory |
| `NaN` rejected on filter parameter arguments | At each `FilterEffectRenderNode` parameter snapshot before division by `CorrectionScale` |
| Negative-where-nonsensical rejected (sigma, radius — not positional offsets) | Same |
| Zero passes through exactly | `0 / scale == 0`; no clamping |
| Sub-pixel positive passes through to Skia | No clamping; Skia handles |
| Numeric convention: `CorrectionScale = bounds / raster` (≥ 1, upscale ratio); transformer divide; compositor multiply (via `SKCanvas.Scale`) | Documented in `contracts/render-node-operation-scale.md` |

## State transitions

None. `RenderScale` is an immutable value type; `RenderNodeOperation.CorrectionScale` is set once at operation creation and immutable thereafter.
