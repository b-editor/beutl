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
    public float ScaleX { get; }
    public float ScaleY { get; }

    public static RenderScale Identity { get; }                    // (1, 1)
    public static RenderScale FromRatio(float ratio);              // uniform (ratio, ratio)
    public static RenderScale FromFrames(PixelSize raster, PixelSize bounds);

    public float ApplyX(float lengthAuthoringSpace);               // = length / ScaleX
    public float ApplyY(float lengthAuthoringSpace);
    public float ApplyUniform(float lengthAuthoringSpace);
    public Size  Apply(Size sizeAuthoringSpace);
    public Point Apply(Point pointAuthoringSpace);
}
```

- **Validation**:
  - Constructor: `ScaleX > 0`, `ScaleY > 0`, both finite. Throws `ArgumentOutOfRangeException` otherwise.
  - `FromFrames(raster, bounds)`: requires per-axis ratios > 0; uniform-scale check (`|sx − sy| ≤ 1e-3 * max(sx, sy)`) emits a warning log (per-clip proxy may have minor non-uniformity due to integer rounding; not an error).
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

## NOT modified (compared to earlier draft, intentionally rolled back)

These types were modified in earlier drafts (commits `a0c20556e` through `d4728ede9`) and are now **left unchanged** by this PR:

- `FilterEffectContext` — no scaled helpers, no `*Raw` twins, no `RenderScale` snapshot, no `ReferenceFrame`.
- `GraphicsContext2D` — no scaled helpers, no `*Raw` twins, no `PushReferenceFrame`. `DrawRectangle(Rect)` etc. record verbatim.
- `IRenderer` — no `ReferenceFrame` property.
- `Renderer` — no new constructor overload taking `referenceFrame`. Constructor signature unchanged.
- `Pen.cs`, `PenHelper.cs` — no `GetScaledThickness` / `GetScaledBounds` family.
- `Transform` subclasses (`TranslateTransform`, `Rotation3DTransform`, `MatrixTransform`, …) — no source changes, no scaling in `CreateMatrix`.
- `CompositionContext` — no `RenderScale` property.
- All `FilterEffect` subclasses (Blur, DropShadow, …) — no source changes (FR-008).
- All `Drawable` / `Shape` subclasses — no source changes.
- `Property` system, animators, property editors — no source changes.

This is a much narrower change surface than prior drafts proposed.

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
| `RenderScale.ScaleX > 0`, `ScaleY > 0`, both finite | `RenderScale` constructor |
| `FromFrames(raster, bounds)` validates `raster.PixelSize > 0` and `bounds.PixelSize > 0` | `FromFrames` factory |
| `NaN` rejected on filter parameter arguments | At each `FilterEffectRenderNode` parameter snapshot before division |
| Negative-where-nonsensical rejected (sigma, radius — not positional offsets) | Same |
| Zero passes through exactly | `0 / scale == 0`; no clamping |
| Sub-pixel positive passes through to Skia | No clamping; Skia handles |

## State transitions

None. `RenderScale` is an immutable value type; `RenderNodeOperation.CorrectionScale` is set once at operation creation and immutable thereafter.
