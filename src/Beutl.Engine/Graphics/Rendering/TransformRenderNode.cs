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
                effectiveScale: RescaleDensity(r.EffectiveScale, Transform)))
            .ToArray();
    }

    /// <summary>
    /// Re-scales a bitmap-backed op's supply density across <paramref name="transform"/> (feature 003,
    /// FR-019). Shared by every transform boundary — this node AND
    /// <c>DrawableGroup.CustomTransformRenderNode</c> — so a scale on a group / decorator wrapper does not
    /// drop a bitmap's density to <see cref="EffectiveScale.Unbounded"/> while a leaf drawable preserves it.
    /// Density is "backing pixels per logical unit": enlarging content (scale &gt; 1) LOWERS it, shrinking
    /// RAISES it (a 4K source dropped into a small box carries its extra detail into a downstream effect — R2).
    /// A single-float density takes the smallest scale factor (the axis preserving the MOST detail), so it can
    /// never under-sample either axis. Pure translation / rotation has scale 1 and leaves density unchanged;
    /// vector (<see cref="EffectiveScale.Unbounded"/>) children re-rasterize at any scale and are unaffected;
    /// a degenerate / perspective matrix yields no clean scale, so density passes through untouched.
    /// </summary>
    public static EffectiveScale RescaleDensity(EffectiveScale input, Matrix transform)
    {
        if (input.IsUnbounded)
            return EffectiveScale.Unbounded;

        float densityFactor = 1f;
        if (transform.TryDecomposeTransform(out _, out Vector scale, out _, out _))
        {
            float f = MathF.Min(MathF.Abs(scale.X), MathF.Abs(scale.Y));
            // Reject a NaN / non-finite / non-positive factor: an infinite scale would yield At(d / ∞) = At(0),
            // a zero working scale downstream. Fall back to the identity factor, leaving density unchanged.
            if (float.IsFinite(f) && f > 0f) densityFactor = f;
        }

        // Guard the QUOTIENT, not just densityFactor: a finite extreme-anisotropic factor can still divide to
        // +inf or a non-positive value, and EffectiveScale.At throws on a non-finite density — the pull path has
        // no try/catch, so it would crash the render. Fall back to the unchanged density rather than failing.
        float d = input.Value / densityFactor;
        if (!float.IsFinite(d) || d <= 0f)
            d = input.Value;

        return EffectiveScale.At(d);
    }
}
