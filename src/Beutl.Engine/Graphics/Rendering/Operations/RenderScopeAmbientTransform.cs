namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares the matrix a guarded scope replays its input under, and how that matrix composes with the ambient
/// transform its ancestors contribute.
/// </summary>
/// <remarks>
/// <see cref="TransformOperator.Prepend"/> is complete as declared. Append and Set depend on the ambient, so
/// <see cref="Requests.AmbientScopeTransformResolver"/> rewrites them into the input-space matrix after recording.
/// What they should mean under a <see cref="RenderScopeTransformSpace.AmbientTarget"/> scope is open:
/// https://github.com/b-editor/beutl/issues/2423.
/// </remarks>
internal readonly record struct RenderScopeAmbientTransform(
    Matrix Matrix,
    TransformOperator Composition,
    Rect? TargetDomain)
{
    /// <summary>Gets whether this declaration needs the ambient transform before it can be replayed.</summary>
    public bool DependsOnAmbient => Composition != TransformOperator.Prepend;

    /// <summary>
    /// Resolves the input-space matrix this scope replays under when its ancestors contribute
    /// <paramref name="ambient"/>.
    /// </summary>
    /// <remarks>
    /// A singular ambient has no inverse, so the declaration stands as written
    /// (https://github.com/b-editor/beutl/issues/2422).
    /// </remarks>
    public Matrix Resolve(Matrix ambient)
    {
        if (!DependsOnAmbient || !ambient.TryInvert(out Matrix inverse))
            return Matrix;

        // Row-vector composition: `a * b` applies a and then b, so `ambient * Matrix * inverse` composed with
        // the ambient is `ambient * Matrix` - the ambient followed by an appended matrix - and
        // `Matrix * inverse` composed with it is `Matrix` alone, which is what Set means.
        return Composition == TransformOperator.Append
            ? ambient * Matrix * inverse
            : Matrix * inverse;
    }

    /// <summary>The ambient a scope replaying under <paramref name="effective"/> hands to its input.</summary>
    public static Matrix Compose(Matrix effective, Matrix ambient) => effective * ambient;

    /// <summary>
    /// Builds the scope a declared transform replays as, over the matrix <see cref="Resolve"/> produced.
    /// </summary>
    /// <param name="declaration">
    /// The declaration the scope keeps, so a later resolution pass reads what was declared rather than what a
    /// previous pass derived.
    /// </param>
    /// <param name="effective">The input-space matrix the scope replays under.</param>
    /// <param name="capturesBackingTarget">
    /// Whether a built-in backdrop inside this scope reads the backing target from outside its materialization.
    /// </param>
    public static TargetScopeDescription CreateScope(
        RenderScopeAmbientTransform declaration,
        Matrix effective,
        bool capturesBackingTarget)
    {
        Matrix inverse = effective.HasInverse ? effective.Invert() : default;
        var state = new AffineScopeState(effective, inverse, declaration.TargetDomain);
        RenderBoundsContract bounds = effective.HasInverse
            ? RenderBoundsContract.Create(
                state,
                static (state, value) => state.TransformBounds(value),
                static (state, value) => state.GetRequiredInputBounds(value))
            : RenderBoundsContract.CreateFullInput(
                state,
                static (state, value) => state.TransformBounds(value));

        return TargetScopeDescription.CreateValueReplayMap(
            effective,
            ExecuteTransform,
            bounds,
            RenderHitTestContract.Custom(
                state,
                static (state, context, point) => state.HitTest(context, point)),
            RenderScaleContract.MapInputSupply(
                state,
                static (state, supply) => TransformRenderNode.RescaleDensity(supply, state.Transform),
                static (state, demand) => TransformRenderNode.RescaleDemand(demand, state.Transform)),
            RenderDeviceGridSensitivity.Insensitive,
            effective.IsIdentity
                ? RenderDeviceGridMapping.Preserved
                : RenderDeviceGridMapping.Remapped,
            capturesBackingTarget,
            ambientTransform: declaration);
    }

    private static void ExecuteTransform(TargetScopeSession session, Matrix transform)
    {
        session.Canvas.Use(canvas =>
        {
            using (canvas.PushTransform(transform))
            {
                session.ReplayInput();
            }
        });
    }

    private readonly record struct AffineScopeState(Matrix Transform, Matrix Inverse, Rect? DeliveredTo)
    {
        public Rect TransformBounds(Rect value) => value.TransformToDeliveredAABB(Transform, DeliveredTo);

        public Rect GetRequiredInputBounds(Rect value) => value.TransformToAABB(Inverse);

        public bool HitTest(RenderHitTestContext context, Point point)
            => Transform.HasInverse && context.Inputs[0].HitTest(point * Inverse);
    }
}
