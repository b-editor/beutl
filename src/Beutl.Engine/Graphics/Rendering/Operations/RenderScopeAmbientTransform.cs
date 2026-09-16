namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares the matrix a guarded scope replays its input under, and how that matrix composes with the ambient
/// transform its ancestors contribute.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TransformOperator.Prepend"/> is expressed in the input's own logical space, so it is complete on
/// its own: the scope maps its input by <see cref="Matrix"/> whatever its ancestors do. The other two are
/// defined against the ambient transform, and recording cannot see it - a graph is recorded bottom-up, and an
/// ancestor that derives its own matrix from measured content has none until its recording completes. Such a
/// scope is recorded provisionally and resolved once the whole graph exists, by
/// <see cref="Requests.AmbientScopeTransformResolver"/>.
/// </para>
/// <para>
/// Resolution rewrites every composition into the input-space matrix that reaches the same destination, so
/// bounds, hit testing, density and replay all read one matrix expressed in one space. The ambient here is the
/// scene's own transform stack and excludes the device grid: a scope's placement is a logical relationship,
/// and folding the target's density into it would make measurement depend on the resolution it is measured at.
/// The resolved matrix is composed with whatever the canvas already carries, so an engine-owned raster
/// alignment that is not part of the scene survives a <see cref="TransformOperator.Set"/> below it.
/// </para>
/// <para>
/// The ambient is what the scene's transform scopes declare, and a scope that places its own replay by writing
/// the destination matrix instead - which is what <see cref="RenderScopeTransformSpace.AmbientTarget"/> names -
/// contributes nothing to it, because there is no matrix to compose. A composition nested under such a scope
/// therefore composes against the scene's declared transforms alone. Measurement and execution still agree
/// there, since the resolved matrix composes with whatever the canvas carries and the graph composes through
/// that scope's own declared bounds; what the operator should mean across it is
/// https://github.com/b-editor/beutl/issues/2423.
/// </para>
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
    /// A singular ambient has no input-space matrix at all - every product with a singular matrix is singular -
    /// and the ancestor that holds it already measures the subtree as empty, because with no inverse it
    /// declares a full-input contract whose forward mapping collapses whatever its input reports. The
    /// declaration therefore stands as written there, and a <see cref="TransformOperator.Set"/> stays collapsed
    /// rather than escaping; letting it escape means detaching the subtree from its ancestors' bounds
    /// composition, which is tracked by https://github.com/b-editor/beutl/issues/2422.
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
