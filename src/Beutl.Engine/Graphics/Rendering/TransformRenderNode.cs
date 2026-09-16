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

        if (changed)
        {
            MarkChanged();
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        var declaration = new RenderScopeAmbientTransform(Transform, TransformOperator, context.TargetDomain);
        // Only Prepend is complete as declared. Append and Set are defined against the ambient transform,
        // which no bottom-up recording can see, so they are recorded over the matrix as written and rewritten
        // into their input-space equivalent once the whole graph exists.
        context.PublishMappedInputs(
            RenderScopeAmbientTransform.CreateScope(
                declaration,
                declaration.Resolve(Matrix.Identity),
                capturesBackingTarget: false),
            static (context, input, value) => context.TargetScope(input, value));
    }

    /// <summary>
    /// Re-scales a bitmap supply density across <paramref name="transform"/>. Enlarging lowers density;
    /// shrinking raises it. Vector (Unbounded) inputs pass through unchanged. An anisotropic transform is
    /// reported through its least-scaled axis, so the result is the density of the best-preserved direction.
    /// </summary>
    public static EffectiveScale RescaleDensity(EffectiveScale input, Matrix transform)
    {
        if (input.IsUnbounded)
            return EffectiveScale.Unbounded;

        float densityFactor = 1f;
        if (transform.TryDecomposeTransform(out _, out Vector scale, out _, out _))
        {
            float f = MathF.Min(MathF.Abs(scale.X), MathF.Abs(scale.Y));
            // Reject non-finite / non-positive factors to avoid zero or NaN density.
            if (float.IsFinite(f) && f > 0f) densityFactor = f;
        }

        // Guard the quotient: extreme factors can still yield +inf or non-positive.
        float d = input.Value / densityFactor;
        if (!float.IsFinite(d) || d <= 0f)
            d = input.Value;

        return EffectiveScale.At(d);
    }

    /// <summary>
    /// Re-scales an output demand back across <paramref name="transform"/> into the input demand that satisfies
    /// it. Enlarging raises the demand; shrinking lowers it. An anisotropic transform is answered through its
    /// operator norm, so the demand covers the most-stretched direction. A perspective transform has no single
    /// scalar density, so its demand passes through unchanged.
    /// </summary>
    /// <remarks>
    /// This is the backward half of the density relationship <see cref="RescaleDensity"/> maps forward, not its
    /// inverse: each half errs toward more detail through a different axis, so under an anisotropic or sheared
    /// transform a forward-then-backward round trip does not return its input.
    /// </remarks>
    public static EffectiveScale RescaleDemand(EffectiveScale outputDemand, Matrix transform)
    {
        if (DeviceGridAlignment.IsPerspective(transform))
            return outputDemand;

        float factor = DeviceGridAlignment.ResolveAffineDensity(transform, 1f);
        float density = outputDemand.Value * factor;
        return float.IsFinite(density) && density > 0f
            ? EffectiveScale.At(density)
            : outputDemand;
    }
}
