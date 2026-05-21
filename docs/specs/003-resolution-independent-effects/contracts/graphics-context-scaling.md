# Contract: GraphicsContext2D Helper Scaling

**Surface**: every length-taking method on `Beutl.Graphics.Rendering.GraphicsContext2D` — `DrawRectangle(Rect, ...)`, `DrawEllipse(Rect, ...)`, `PushTransform(Matrix, ...)`, `PushTransform(Transform.Resource, ...)`, `PushClip(Rect, ...)`, `PushLayer(Rect)`, `PushOpacityMask(..., Rect, ...)` — and their `*Raw` twins.

**Audience**: direct callers of `GraphicsContext2D` (custom `Drawable` subclasses, custom container effects, plugins that draw without going through a built-in Shape or FilterEffect).

> This file is a sibling of `effect-helper-scaling.md`. Same design pattern (helper-internal scaling + `*Raw` twin), applied to `GraphicsContext2D` rather than `FilterEffectContext`.

## The contract in one paragraph

Every length-taking helper on `GraphicsContext2D` interprets its length-typed argument as **"pixels measured against the project's export resolution"**. The implementation multiplies by `this.RenderScale` before forwarding to the underlying render-node / Skia builder. A `*Raw` variant of every such helper passes its arguments through verbatim, for callers that want raw-raster pixel semantics.

## When the new behavior triggers

`RenderScale.Identity` everywhere today → multiplication is a no-op → behavior is byte-identical to before. Becomes observable when a future proxy-preview UX constructs a renderer with `RenderScale ≠ Identity`.

## Surface — scaled helpers

Signatures are unchanged from `Beutl.Engine` pre-feature:

```csharp
namespace Beutl.Graphics.Rendering
{
    public sealed partial class GraphicsContext2D : IDisposable, IPopable
    {
        public RenderScale RenderScale  { get; } // top of stack
        public PixelSize  ReferenceFrame { get; } // top of stack

        // Length-taking helpers — now apply RenderScale internally.
        public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
        public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
        public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);
        public PushedState PushTransform(Transform.Resource transform, TransformOperator transformOperator = TransformOperator.Prepend);
        public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);
        public PushedState PushLayer(Rect limit = default);
        public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false);

        // Existing PushClip(Geometry.Resource), PushOpacity(float), PushBlendMode(BlendMode), PushFilterEffect(...),
        // DrawImageSource(...), DrawVideoSource(...), DrawDrawable(...), DrawNode(...), DrawBackdrop(...),
        // DrawGeometry(...), DrawText(...) are unchanged — Geometry / Text / Brush content scaling is deferred.
    }
}
```

For `Rect` arguments: both the position (`X`, `Y`) and the extent (`Width`, `Height`) are scaled per axis (`X * ScaleX`, `Y * ScaleY`, `Width * ScaleX`, `Height * ScaleY`).

For `Matrix` arguments: only the translation column (`M31` / `M32` in row-major) is scaled. Rotation / scale / skew columns are dimensionless and unchanged. The scaled matrix preserves orthogonality and determinant orientation.

For `PushTransform(Transform.Resource)`: **the resource's matrix is read verbatim — no re-scaling here.** The Transform already scaled its translation at `CreateMatrix` time (see `transform-scaling.md`); scaling again would double-apply. The `*Raw` twin (`PushTransformRaw(Transform.Resource)`) also reads verbatim but is documented as the explicit "bypass any future re-scaling we might add at this site" escape hatch.

## Surface — `*Raw` opt-out twins

```csharp
public sealed partial class GraphicsContext2D
{
    public void DrawRectangleRaw(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
    public void DrawEllipseRaw(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
    public PushedState PushTransformRaw(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);
    public PushedState PushTransformRaw(Transform.Resource transform, TransformOperator transformOperator = TransformOperator.Prepend);
    public PushedState PushClipRaw(Rect clip, ClipOperation operation = ClipOperation.Intersect);
    public PushedState PushLayerRaw(Rect limit = default);
    public PushedState PushOpacityMaskRaw(Brush.Resource mask, Rect bounds, bool invert = false);
}
```

`*Raw` helpers pass arguments verbatim. Same `NaN` guard at the boundary; no scaling, no rasterizer-minimum clamp.

## Built-in callers that benefit automatically

- `RectShape.Render`, `EllipseShape.Render`, `RoundedRectShape.Render` all call `DrawRectangle` / `DrawEllipse` — scaled automatically.
- `Drawable.Render` paths that push clip / layer / opacity-mask regions get scaled automatically.
- `SceneDrawable`, `LayerEffect` continue to push the appropriate `ReferenceFrame` per `render-scale.md`.

## Sub-pixel / zero / NaN handling

Same rules as `effect-helper-scaling.md`:

- Zero passes through exactly.
- Sub-pixel positive values pass through (the rasterizer handles them).
- `NaN` in any axis throws `ArgumentException`.
- Negative `Width` / `Height` on `Rect` arguments are rejected (`ArgumentOutOfRangeException`); negative `X` / `Y` (positions) pass through.
- `*Raw` variants apply the `NaN` guard but skip the negative-extent guard.

## Backward compatibility

- Today (`RenderScale.Identity`): byte-identical.
- After proxy-preview UX: existing custom Drawables / containers automatically become resolution-independent. The only callers that break are ones that explicitly relied on raw-raster pixel semantics — those switch to the `*Raw` twin (one method-name suffix change per call site).
