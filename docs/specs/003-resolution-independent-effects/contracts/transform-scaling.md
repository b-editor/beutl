# Contract: Transform Scaling at Render-Node Application

**Surface**: `Beutl.Graphics.Transformation.Transform.CreateMatrix(CompositionContext)` (unchanged from pre-feature); `Beutl.Graphics.Rendering.TransformRenderNode` (gains an `IsRaw` flag); `Beutl.Graphics.ImmediateCanvas.PushTransform(Matrix, …)` (multiplies translation column by `RenderScale` unless the source node is `IsRaw`).

**Audience**: anyone authoring a custom `Transform` subclass, anyone reading `Transform.Resource.Matrix`, anyone implementing a custom renderer that consumes `TransformRenderNode`.

> Design rationale: see `research.md` § R10 — decision (c), scale at render-node application time in `ImmediateCanvas`. Previous drafts of this contract specified materialize-time scaling in `Transform.CreateMatrix(CompositionContext)`; that approach was abandoned after design review surfaced (i) an unclosed `CompositionContext.RenderScale` propagation chain, (ii) silent scaling of bounding-box computations, and (iii) a surface-by-surface inconsistency in the plugin migration contract. Render-node-application time resolves all three.

## The contract in one paragraph

`Transform.Resource.Matrix` stores the **authoring-space** matrix — translation column in export-resolution pixels, exactly as the user authored it. `TransformRenderNode.Transform` is the same matrix, captured at render-node creation time. **Scaling happens once, at the very bottom**: when `ImmediateCanvas.PushTransform(matrix, …)` actually pushes the matrix to the underlying `SKCanvas`, it multiplies the translation column by `this.RenderScale` (unless the node is flagged `IsRaw`, in which case it pushes verbatim). Custom `Transform` subclasses do not have to know about `RenderScale`. Bounding-box computations that read `Transform.Resource.Matrix` directly get authoring-space values, which is what they want for project-space math.

## When the new behavior triggers

Today `RenderScale.Identity` everywhere → the multiplication is a no-op → behavior is byte-identical to before. Becomes observable when a future proxy-preview UX constructs a renderer with `RenderScale ≠ Identity`.

## Surface — `Transform` (unchanged from pre-feature)

```csharp
namespace Beutl.Graphics.Transformation
{
    public abstract class Transform : EngineObject
    {
        // Unchanged: returns a project-space matrix. CompositionContext does NOT
        // gain a RenderScale property; the materialization site is RenderScale-unaware.
        public abstract Matrix CreateMatrix(CompositionContext context);

        public new sealed class Resource : EngineObject.Resource
        {
            public Matrix Matrix { get; set; } = Matrix.Identity; // project-space
            // …
        }
    }

    // Concrete subclasses unchanged:
    public sealed partial class TranslateTransform : Transform
    {
        public IProperty<float> X { get; } = Property.CreateAnimatable(0f);
        public IProperty<float> Y { get; } = Property.CreateAnimatable(0f);

        public override Matrix CreateMatrix(CompositionContext context)
        {
            // Unchanged: returns Matrix.CreateTranslation(X.Value, Y.Value);
        }
    }

    // Likewise Rotation3DTransform, MatrixTransform, RotationTransform, ScaleTransform, SkewTransform.
}
```

No source change to any `Transform` subclass — including third-party ones. Custom Transforms automatically benefit because their authoring-space output is scaled at the render pipeline's edge.

## Surface — `TransformRenderNode` (adds `IsRaw`)

```csharp
namespace Beutl.Graphics.Rendering
{
    public sealed class TransformRenderNode : ContainerRenderNode
    {
        public TransformRenderNode(Matrix transform, TransformOperator transformOperator, bool isRaw = false);

        public Matrix             Transform           { get; private set; } // authoring-space (or raw raster if IsRaw)
        public TransformOperator  TransformOperator   { get; private set; }
        public bool               IsRaw               { get; }              // NEW

        public bool Update(Matrix transform, TransformOperator transformOperator);
        public override RenderNodeOperation[] Process(RenderNodeContext context);
    }
}
```

`IsRaw == false` (default): the node's `Transform` is authoring-space; `ImmediateCanvas.PushTransform` multiplies its translation column by `RenderScale` before pushing.

`IsRaw == true`: the node's `Transform` is raw-raster; `ImmediateCanvas.PushTransform` pushes verbatim. Constructed by `GraphicsContext2D.PushTransformRaw(Matrix, …)` / `PushTransformRaw(Transform.Resource, …)`.

## Surface — `ImmediateCanvas.PushTransform` (does the scaling)

```csharp
namespace Beutl.Graphics
{
    public sealed partial class ImmediateCanvas
    {
        public RenderScale RenderScale { get; } // mirrored from the constructing Renderer

        // Called by TransformRenderNode.Process when processing the node tree.
        // matrix is the node's Transform field; isRaw is the node's IsRaw flag.
        public PushedState PushTransform(Matrix matrix, TransformOperator op, bool isRaw)
        {
            if (!isRaw)
            {
                matrix = ScaleTranslationColumn(matrix, this.RenderScale);
            }
            // … existing SKCanvas.Concat / SetMatrix path …
        }

        private static Matrix ScaleTranslationColumn(Matrix m, RenderScale scale)
            => new Matrix(m.M11, m.M12, m.M21, m.M22,
                          m.M31 * scale.ScaleX,
                          m.M32 * scale.ScaleY);
    }
}
```

Rotation, scale, and skew columns of the matrix are not touched. Composability with other transforms is preserved (Matrix multiplication still works correctly because scaling the translation column commutes with concatenating non-translation transforms in `TransformOperator.Prepend` semantics).

## Surface — `GraphicsContext2D.PushTransform` (records, does NOT scale)

```csharp
namespace Beutl.Graphics.Rendering
{
    public sealed partial class GraphicsContext2D
    {
        public PushedState PushTransform(Matrix matrix, TransformOperator op = TransformOperator.Prepend)
        {
            // Records `matrix` verbatim into TransformRenderNode (IsRaw = false).
            // Scaling happens later in ImmediateCanvas.PushTransform.
        }

        public PushedState PushTransform(Transform.Resource transform, TransformOperator op = TransformOperator.Prepend)
        {
            // Records `transform.Matrix` verbatim into TransformRenderNode (IsRaw = false).
        }

        public PushedState PushTransformRaw(Matrix matrix, TransformOperator op = TransformOperator.Prepend)
        {
            // Records into TransformRenderNode with IsRaw = true.
        }

        public PushedState PushTransformRaw(Transform.Resource transform, TransformOperator op = TransformOperator.Prepend)
        {
            // Records `transform.Matrix` into TransformRenderNode with IsRaw = true.
        }
    }
}
```

`PushTransform(Matrix)` is the one exception to "every `GraphicsContext2D` helper scales at API time" — the Transform path deliberately defers scaling to render-node application. Other helpers (`DrawRectangle(Rect)`, `PushClip(Rect)`, …) continue to scale at API time.

## Why no `*Raw` opt-out at the `Transform` subclass layer

Custom `Transform` subclasses cannot reasonably want raw-raster semantics inside `CreateMatrix` — they don't know `RenderScale` at materialization time anymore. Callers who want raw matrices construct one themselves and push via `PushTransformRaw(Matrix)`, or wrap a raw-authored matrix in a `MatrixTransform` and push via `PushTransformRaw(Transform.Resource)`.

## How a built-in `Transform` looks under the new contract

```csharp
// Unchanged from pre-feature.
public override Matrix CreateMatrix(CompositionContext context)
    => Matrix.CreateTranslation(X.Value, Y.Value);
```

The `(X.Value, Y.Value)` pair is in export-resolution pixels (the user's authoring values). The output Matrix is in authoring space. Rendering scales it.

## `MatrixTransform` and custom user matrices

Same simplification. `MatrixTransform.CreateMatrix` returns `Matrix.Value` verbatim (no scaling). At render time, the translation column is scaled. Users who want raw-raster matrices push via `PushTransformRaw(Transform.Resource)`.

## Backward compatibility

- Today: `RenderScale.Identity` → `ImmediateCanvas.PushTransform` no-op multiplication → matrices reach SKCanvas unchanged → output byte-equivalent (modulo any guards) to pre-feature.
- After proxy preview: every Transform automatically scales correctly. Custom Transform subclasses (third-party plugins) automatically benefit — no source change needed.
- `Transform.Resource.Matrix` semantics: authoring-space. Any code that read it for bounding-box / animation purposes continues to read authoring-space values (which is what those callers want).

## Render-time scaling and caching

`RenderNodeCache` ([`src/Beutl.Engine/Graphics/Rendering/Renderer.cs`](../../../../src/Beutl.Engine/Graphics/Rendering/Renderer.cs) and `Cache/`) keys on the node tree. With render-time scaling, the same `TransformRenderNode` instance produces correctly-scaled output regardless of the renderer's `RenderScale`, so the cache stays valid when proxy resolution changes. Caches do NOT need to be invalidated on `RenderScale` change.
