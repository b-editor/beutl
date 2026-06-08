namespace Beutl.Graphics.Rendering;

public sealed class TransformRenderNode(Matrix transform, TransformOperator transformOperator) : ContainerRenderNode
{
    public Matrix Transform { get; private set; } = transform;

    public TransformOperator TransformOperator { get; private set; } = transformOperator;

    public bool Update(Matrix transform, TransformOperator transformOperator)
    {
        bool changed = false;
        if (Transform != transform)
        {
            Transform = transform;
            changed = true;
        }

        if (TransformOperator != transformOperator)
        {
            TransformOperator = transformOperator;
            changed = true;
        }

        HasChanges = changed;
        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        // feature 003 (FR-019): a transform RE-SCALES the supply density of a bitmap-backed child. Density is
        // "backing pixels per logical unit", so enlarging content (scale > 1) spreads the same backing pixels
        // over more logical units and LOWERS the density it affords; shrinking RAISES it (a 4K source dropped
        // into a small box now carries its extra detail into a downstream effect — R2). A single-float density
        // projects an anisotropic / rotated transform onto the axis that preserves the MOST detail — the
        // smallest scale factor → the highest density — so it can never under-sample either axis. A pure
        // translation / rotation has scale 1 on both axes and leaves the density unchanged. Vector (Unbounded)
        // children re-rasterize at any scale and are unaffected. A degenerate / perspective matrix yields no
        // clean scale, so the density passes through untouched.
        float densityFactor = 1f;
        if (Transform.TryDecomposeTransform(out _, out Vector scale, out _, out _))
        {
            float f = MathF.Min(MathF.Abs(scale.X), MathF.Abs(scale.Y));
            if (f > 0f) densityFactor = f;
        }

        return context.Input.Select(r =>
            RenderNodeOperation.CreateLambda(
                r.Bounds.TransformToAABB(Transform),
                canvas =>
                {
                    using (canvas.PushTransform(Transform, TransformOperator))
                    {
                        r.Render(canvas);
                    }
                },
                hitTest: point =>
                {
                    if (Transform.HasInverse)
                        point *= Transform.Invert();
                    return r.HitTest(point);
                },
                onDispose: r.Dispose,
                effectiveScale: r.EffectiveScale.IsUnbounded
                    ? EffectiveScale.Unbounded
                    : EffectiveScale.At(r.EffectiveScale.Value / densityFactor)))
            .ToArray();
    }
}
