# Contract: GraphicsContext2D Helper Scaling

**Surface (Rect helpers)**: `DrawRectangle(Rect, …)`, `DrawEllipse(Rect, …)`, `PushClip(Rect, …)`, `PushLayer(Rect)`, `PushOpacityMask(…, Rect, …)` — and their `*Raw` twins. **Scaled at API call time** by `this.RenderScale`.

**Surface (Transform path)**: `PushTransform(Matrix, …)` and `PushTransform(Transform.Resource, …)` — and their `*Raw` twins. **NOT scaled at API call time.** The matrix is recorded verbatim into `TransformRenderNode`; scaling happens later at render-node application inside `ImmediateCanvas.PushTransform`. See `transform-scaling.md` for the authoritative Transform contract.

**Audience**: direct callers of `GraphicsContext2D` (custom `Drawable` subclasses, custom container effects, plugins that draw without going through a built-in Shape or FilterEffect).

> This file is a sibling of `effect-helper-scaling.md` (same design pattern: helper-internal scaling + `*Raw` twin, applied to Rect helpers on `GraphicsContext2D`). The Transform path on `GraphicsContext2D` is **the documented exception** — it defers to `transform-scaling.md` per `research.md` § R10 (revised after design review).

## The contract in one paragraph

**Rect helpers** on `GraphicsContext2D` interpret their `Rect` argument as "pixels at the project's export resolution" and multiply by `this.RenderScale` before forwarding to the render-node / Skia builder. A `*Raw` variant passes the `Rect` through verbatim. **Transform helpers** (`PushTransform`) are a deliberate exception: they record the matrix verbatim into `TransformRenderNode`, and the scaling happens once, at the bottom of the rendering pipeline, in `ImmediateCanvas.PushTransform`. This split is intentional — see `research.md` § R10 for why Transforms benefit from render-node-application scaling (cache validity across `RenderScale` changes, custom `Transform` subclasses participate automatically, `Transform.Resource.Matrix` stays authoring-space for project-space consumers).

## When the new behavior triggers

`RenderScale.Identity` everywhere today → multiplication is a no-op → behavior is visually equivalent to before (SSIM ≥ 0.97; new `NaN` / negative-extent guards excepted). Becomes observable when a future proxy-preview UX constructs a renderer with `RenderScale ≠ Identity`.

## Surface — Rect helpers (scaled at API call time)

Signatures are unchanged from `Beutl.Engine` pre-feature:

```csharp
namespace Beutl.Graphics.Rendering
{
    public sealed partial class GraphicsContext2D : IDisposable, IPopable
    {
        public RenderScale RenderScale  { get; } // top of stack
        public PixelSize  ReferenceFrame { get; } // top of stack

        // Rect helpers — apply RenderScale internally at API call time.
        public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
        public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
        public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect);
        public PushedState PushLayer(Rect limit = default);
        public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false);

        // PushTransform path — DOES NOT scale here. See transform-scaling.md.
        public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend);
        public PushedState PushTransform(Transform.Resource transform, TransformOperator transformOperator = TransformOperator.Prepend);

        // Existing PushClip(Geometry.Resource), PushOpacity(float), PushBlendMode(BlendMode), PushFilterEffect(...),
        // DrawImageSource(...), DrawVideoSource(...), DrawDrawable(...), DrawNode(...), DrawBackdrop(...),
        // DrawGeometry(...), DrawText(...) are unchanged — Geometry / Text / Brush content scaling is deferred.
    }
}
```

For `Rect` arguments to the Rect helpers above: both the position (`X`, `Y`) and the extent (`Width`, `Height`) are scaled per axis (`X * ScaleX`, `Y * ScaleY`, `Width * ScaleX`, `Height * ScaleY`).

## Surface — Transform helpers (Transform path — record only, scale later)

```csharp
public sealed partial class GraphicsContext2D
{
    // Both overloads RECORD the matrix verbatim into TransformRenderNode (IsRaw = false).
    // ImmediateCanvas.PushTransform scales the translation column at render-node application time.
    public PushedState PushTransform(Matrix matrix, TransformOperator op = TransformOperator.Prepend);
    public PushedState PushTransform(Transform.Resource transform, TransformOperator op = TransformOperator.Prepend);

    // PushTransformRaw records with IsRaw = true; ImmediateCanvas bypasses scaling for raw nodes.
    public PushedState PushTransformRaw(Matrix matrix, TransformOperator op = TransformOperator.Prepend);
    public PushedState PushTransformRaw(Transform.Resource transform, TransformOperator op = TransformOperator.Prepend);
}
```

The Transform path is the **one documented exception** to "every `GraphicsContext2D` helper scales at API call time". Full rationale and the render-node-application contract live in `transform-scaling.md`. Do not duplicate that contract here — when in doubt, `transform-scaling.md` wins.

## Surface — `*Raw` opt-out twins for Rect helpers

```csharp
public sealed partial class GraphicsContext2D
{
    public void DrawRectangleRaw(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
    public void DrawEllipseRaw(Rect rect, Brush.Resource? fill, Pen.Resource? pen);
    public PushedState PushClipRaw(Rect clip, ClipOperation operation = ClipOperation.Intersect);
    public PushedState PushLayerRaw(Rect limit = default);
    public PushedState PushOpacityMaskRaw(Brush.Resource mask, Rect bounds, bool invert = false);
}
```

`*Raw` Rect helpers pass arguments verbatim. Same `NaN` guard at the boundary; no scaling, no rasterizer-minimum clamp.

(For `PushTransformRaw`, see the Transform path section above — it sets `IsRaw = true` on the resulting `TransformRenderNode`.)

## Built-in callers that benefit automatically

- `RectShape.Render`, `EllipseShape.Render`, `RoundedRectShape.Render` all call `DrawRectangle` / `DrawEllipse` — scaled automatically at API call time.
- `Drawable.Render` paths that push clip / layer / opacity-mask regions get scaled automatically at API call time.
- Any caller using `Transform` (built-in subclasses or custom plugin subclasses) benefits automatically via the render-node-application path — no source change required.
- `SceneDrawable`, `LayerEffect` continue to push the appropriate `ReferenceFrame` per `render-scale.md`. They additionally MUST construct sub-canvases / sub-renderers with `referenceFrame = subScene.FrameSize` so that the inner `ImmediateCanvas.RenderScale` reflects the inner scene — see `render-scale.md` § "Nested-scene Transform consistency".

## Sub-pixel / zero / NaN handling

Same rules as `effect-helper-scaling.md`:

- Zero passes through exactly.
- Sub-pixel positive values pass through (the rasterizer handles them).
- `NaN` in any axis throws `ArgumentException`.
- Negative `Width` / `Height` on `Rect` arguments are rejected (`ArgumentOutOfRangeException`); negative `X` / `Y` (positions) pass through.
- `*Raw` variants apply the `NaN` guard but skip the negative-extent guard.

## Backward compatibility

- Today (`RenderScale.Identity`): visually equivalent to before (SSIM ≥ 0.97 per FR-003 / SC-002). New `NaN` / negative-extent guards apply only to previously-undefined inputs.
- After proxy-preview UX: existing custom Drawables / containers automatically become resolution-independent. The only callers that break are ones that explicitly relied on raw-raster pixel semantics — those switch to the `*Raw` twin (one method-name suffix change per call site).
