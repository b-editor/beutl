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

    public override void Process(RenderNodeContext context)
    {
        Matrix transform = Transform;
        TransformOperator transformOperator = TransformOperator;
        Matrix inverse = transform.HasInverse ? transform.Invert() : default;
        var metadataState = new TransformMetadataState(transform, transform.HasInverse, inverse);
        RenderBoundsContract bounds = transform.HasInverse
            ? RenderBoundsContract.Create(
                metadataState.TransformBounds,
                metadataState.GetRequiredInputBounds,
                structuralKey: (typeof(TransformRenderNode), "invertible-bounds"))
            : RenderBoundsContract.CreateFullInput(
                metadataState.TransformBounds,
                structuralKey: (typeof(TransformRenderNode), "singular-bounds"));
        RenderHitTestContract hitTest = RenderHitTestContract.Custom(
            metadataState.HitTest,
            structuralKey: typeof(TransformRenderNode));
        RenderScaleContract scale = RenderScaleContract.MapInputSupply(
            new TransformScaleMapper(transform).Map,
            structuralKey: typeof(TransformRenderNode));
        var runtimeIdentity = new RenderRuntimeIdentity((transform, transformOperator));

        foreach (RenderFragmentHandle input in context.Inputs)
        {
            Action<TargetScopeSession> execute = session => session.Canvas.Use(canvas =>
            {
                using (canvas.PushTransform(transform, transformOperator))
                {
                    session.ReplayInput();
                }
            });
            TargetScopeDescription description = transformOperator == TransformOperator.Prepend
                ? TargetScopeDescription.CreateValueReplayMap(
                    execute,
                    bounds,
                    hitTest,
                    scale,
                    structuralKey: typeof(TransformRenderNode),
                    runtimeIdentity: runtimeIdentity)
                : TargetScopeDescription.Create(
                    execute,
                    bounds,
                    hitTest,
                    scale,
                    structuralKey: typeof(TransformRenderNode),
                    runtimeIdentity: runtimeIdentity);
            context.Publish(context.TargetScope(input, description));
        }
    }

    /// <summary>
    /// Re-scales a bitmap supply density across <paramref name="transform"/>. Enlarging lowers density;
    /// shrinking raises it. Vector (Unbounded) inputs pass through unchanged.
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

    private readonly record struct TransformMetadataState(
        Matrix Transform,
        bool HasInverse,
        Matrix Inverse)
    {
        public Rect TransformBounds(Rect value) => value.TransformToAABB(Transform);

        public Rect GetRequiredInputBounds(Rect value) => value.TransformToAABB(Inverse);

        public bool HitTest(RenderHitTestContext metadata, Point point)
        {
            if (HasInverse)
                point *= Inverse;
            return metadata.Inputs[0].HitTest(point);
        }
    }

    private readonly record struct TransformScaleMapper(Matrix Transform)
    {
        public EffectiveScale Map(EffectiveScale inputSupply)
            => RescaleDensity(inputSupply, Transform);
    }
}
