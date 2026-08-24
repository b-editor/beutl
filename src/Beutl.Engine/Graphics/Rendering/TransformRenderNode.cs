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
            HasChanges = true;
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        Matrix transform = Transform;
        TransformOperator transformOperator = TransformOperator;
        Matrix inverse = transform.HasInverse ? transform.Invert() : default;
        var metadataState = new TransformMetadataState(
            transform,
            transform.HasInverse,
            inverse,
            context.TargetDomain);
        RenderBoundsContract bounds = transform.HasInverse
            ? RenderBoundsContract.Create(
                metadataState,
                static (state, value) => state.TransformBounds(value),
                static (state, value) => state.GetRequiredInputBounds(value))
            : RenderBoundsContract.CreateFullInput(
                metadataState,
                static (state, value) => state.TransformBounds(value));
        RenderHitTestContract hitTest = RenderHitTestContract.Custom(
            metadataState,
            static (state, context, point) => state.HitTest(context, point));
        var scaleMapper = new TransformScaleMapper(transform);
        RenderScaleContract scale = RenderScaleContract.MapInputSupply(
            scaleMapper,
            static (mapper, supply) => mapper.MapSupply(supply),
            static (mapper, demand) => mapper.MapDemand(demand));
        // Set discards the ambient transform for the canvas base transform, so it moves the input even when
        // the matrix is identity.
        RenderDeviceGridMapping gridMapping =
            transform.IsIdentity && transformOperator != TransformOperator.Set
                ? RenderDeviceGridMapping.Preserved
                : RenderDeviceGridMapping.Remapped;

        // Only Prepend places its matrix in the input's own logical space. Append and Set are defined
        // against the ambient target transform, which the value graph has no representation of.
        if (transformOperator == TransformOperator.Prepend)
        {
            TargetScopeDescription description = TargetScopeDescription.CreateValueReplayMap(
                (transform, transformOperator),
                ExecuteTransform,
                bounds,
                hitTest,
                scale,
                RenderDeviceGridSensitivity.Insensitive,
                gridMapping,
                builtInBackdropCapturesBackingTarget: false);
            context.PublishMappedInputs(
                description,
                static (context, input, value) => context.TargetScope(input, value));
            return;
        }

        TargetScopeDefinition<(Matrix Transform, TransformOperator Operator)> definition =
            TargetScopeDefinition<(Matrix Transform, TransformOperator Operator)>.Create(
                ExecuteTransform,
                bounds,
                hitTest,
                scale,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                deviceGridMapping: gridMapping);
        context.PublishMappedInputs(
            definition.Call((transform, transformOperator)),
            static (context, input, value) => context.TargetScope(input, value));
    }

    private static void ExecuteTransform(
        TargetScopeSession session,
        (Matrix Transform, TransformOperator Operator) state)
    {
        session.Canvas.Use(canvas =>
        {
            using (canvas.PushTransform(state.Transform, state.Operator))
            {
                session.ReplayInput();
            }
        });
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

    private readonly record struct TransformMetadataState(
        Matrix Transform,
        bool HasInverse,
        Matrix Inverse,
        Rect? DeliveredTo)
    {
        public Rect TransformBounds(Rect value) => value.TransformToDeliveredAABB(Transform, DeliveredTo);

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
        public EffectiveScale MapSupply(EffectiveScale inputSupply)
            => RescaleDensity(inputSupply, Transform);

        public EffectiveScale MapDemand(EffectiveScale outputDemand)
            => RescaleDemand(outputDemand, Transform);
    }
}
