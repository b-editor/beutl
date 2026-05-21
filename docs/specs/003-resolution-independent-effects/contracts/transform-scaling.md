# Contract: Transform Scaling at CreateMatrix

**Surface**: `Beutl.Graphics.Transformation.Transform.CreateMatrix(CompositionContext)` and its concrete subclasses (`TranslateTransform`, `Rotation3DTransform`, `MatrixTransform`); `CompositionContext` gains a `RenderScale` property.

**Audience**: anyone authoring a custom `Transform` subclass, or anyone reading `Transform.Resource.Matrix`.

> Design rationale: see `research.md` § R10 — scale at `CreateMatrix`, not at the push site.

## The contract in one paragraph

A `Transform.Resource.Matrix` is the materialized form of a `Transform`. Its translation column (`M31`, `M32` in row-major) is **already scaled** when the resource is produced — `TranslateTransform.CreateMatrix` returns `Matrix.CreateTranslation(X * scale.ScaleX, Y * scale.ScaleY)` instead of `Matrix.CreateTranslation(X, Y)`. The scale comes from `CompositionContext.RenderScale`, set by the surrounding scene composition. Pure-rotation, pure-scale, pure-skew transforms have no translation component and are unchanged.

## Surface — `CompositionContext`

```csharp
namespace Beutl.Graphics.Effects // or wherever CompositionContext lives
{
    public sealed class CompositionContext
    {
        // … existing members …

        // NEW — the RenderScale to apply at materialization time. Sourced from the
        // active scene's (FrameSize, ReferenceFrame) pair via SceneCompositor.
        public RenderScale RenderScale { get; }
    }
}
```

The scene composition path (`SceneCompositor` → `SceneRenderer` → `Renderer`) plumbs `RenderScale` into the `CompositionContext` it produces. For top-level scenes this is identity today; for nested compositions / `LayerEffect` it is whatever the surrounding `PushReferenceFrame` declared.

## Surface — Transform subclasses

```csharp
public sealed partial class TranslateTransform : Transform
{
    public IProperty<float> X { get; } = Property.CreateAnimatable(0f);
    public IProperty<float> Y { get; } = Property.CreateAnimatable(0f);

    public override Matrix CreateMatrix(CompositionContext context)
    {
        float x = X.Value;
        float y = Y.Value;
        return Matrix.CreateTranslation(x * context.RenderScale.ScaleX, y * context.RenderScale.ScaleY);
    }
}

public sealed partial class Rotation3DTransform : Transform
{
    // CenterX / CenterY / CenterZ / Depth are pixel-absolute → scale at CreateMatrix.
    // RotationX / RotationY / RotationZ are degrees → unchanged.
    public override Matrix CreateMatrix(CompositionContext context)
    {
        // … existing perspective matrix construction, but with
        //   scaledCx = CenterX.Value * context.RenderScale.ScaleX
        //   scaledCy = CenterY.Value * context.RenderScale.ScaleY
        //   scaledCz = CenterZ.Value * context.RenderScale.ApplyUniform(1)
        //   scaledDepth = Depth.Value * context.RenderScale.ApplyUniform(1)
        // … rest unchanged …
    }
}

public sealed partial class MatrixTransform : Transform
{
    public IProperty<Matrix> Matrix { get; } = Property.CreateAnimatable(Graphics.Matrix.Identity);

    public override Matrix CreateMatrix(CompositionContext context)
    {
        var m = Matrix.Value;
        // Multiply the translation column by RenderScale; rotation / scale / skew columns unchanged.
        return new Matrix(m.M11, m.M12, m.M21, m.M22,
                          m.M31 * context.RenderScale.ScaleX,
                          m.M32 * context.RenderScale.ScaleY);
    }
}
```

`RotationTransform`, `ScaleTransform`, `SkewTransform` have no translation component and are not modified.

## Why no `*Raw` opt-out at the Transform layer

There is no realistic use case for "a `TranslateTransform` whose `X` means raw raster pixels at this current proxy size". Such a use case would mean "I want my transform to behave differently between proxy and export", which is exactly what this feature exists to prevent.

Callers who genuinely need an unscaled translation use `GraphicsContext2D.PushTransformRaw(Matrix)` directly with their own matrix.

## `MatrixTransform` and custom user matrices

Users who author a `MatrixTransform` via `IProperty<Matrix>` will have their matrix's translation column scaled by `CreateMatrix`. This is the correct behavior: the user authored a matrix in export-resolution coordinates, and at proxy time the engine scales appropriately. The translation is the only column that is pixel-typed; rotation / scale / skew are dimensionless.

If a user genuinely authored a matrix in raw raster pixels and does not want scaling, they should:

- Push the matrix directly through `GraphicsContext2D.PushTransformRaw(matrix)` rather than wrapping it in a `MatrixTransform`, **or**
- Document the use case so we can consider an opt-out in a follow-up.

## Backward compatibility

- Today: `RenderScale.Identity` → all multiplications are no-ops → produced matrices are byte-identical to before → no observable change.
- After proxy preview: every transform automatically scales correctly. Projects that animate `TranslateTransform.X / Y` keep their authored values; the renderer scales internally.
- Existing custom `Transform` subclasses (third-party plugins): if their `CreateMatrix` overrides include translation, they would *not* automatically benefit because the scaling lives inside each built-in subclass's `CreateMatrix`. Plugin authors who want to participate update their `CreateMatrix` to multiply by `context.RenderScale` themselves. The plugin-author migration guide documents this — same one-line change pattern as the built-in subclasses.
