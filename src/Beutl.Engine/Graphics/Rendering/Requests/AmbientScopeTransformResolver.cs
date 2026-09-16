namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Rewrites every scope whose declared transform composes against the ambient transform into the input-space
/// scope that reaches the same destination.
/// </summary>
/// <remarks>
/// <para>
/// A graph is recorded bottom-up, so a scope is described before anything above it exists, and an ancestor
/// such as <see cref="DrawableGroup"/>'s alignment transform derives its own matrix from content measured
/// below it. <see cref="TransformOperator.Append"/> and <see cref="TransformOperator.Set"/> are defined
/// against that ambient, so they cannot be described where they are recorded - which is why they were
/// previously measured in one space and replayed in another.
/// </para>
/// <para>
/// Running top-down over the finished graph closes that gap: every scope above is already described, so the
/// ambient is the product of their resolved matrices, and each composition collapses into a single matrix the
/// scope's bounds, hit test, density and replay all read. Resolution reads the declaration rather than the
/// matrix a previous pass derived, so running it again over an already-resolved graph answers the same.
/// </para>
/// </remarks>
internal static class AmbientScopeTransformResolver
{
    public static void Resolve(IReadOnlyList<RenderFragmentReference> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var visited = new HashSet<VisitedAmbient>();
        var resolved = new Dictionary<RenderFragmentReference, Matrix>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
            Visit(root, Matrix.Identity, visited, resolved);
    }

    private static void Visit(
        RenderFragmentReference reference,
        Matrix ambient,
        HashSet<VisitedAmbient> visited,
        Dictionary<RenderFragmentReference, Matrix> resolved)
    {
        // A fragment reachable twice under one ambient resolves to one answer, and a shared fragment reached
        // under two of them has to be walked under both: what its own descendants compose against differs.
        if (!visited.Add(new VisitedAmbient(reference, ambient)))
            return;

        Matrix inputAmbient = ambient;
        if (reference.Payload is TargetScopeRenderFragmentPayload payload
            && payload.Description.AmbientTransform is { } declaration)
        {
            Matrix effective = declaration.Resolve(ambient);
            if (declaration.DependsOnAmbient)
            {
                // A fragment holds one description, so a scope shared by consumers that contribute the same
                // ambient resolves once and answers both. Two ambients have no such answer, and overwriting
                // the first would hand one consumer the other's matrix without a word.
                if (resolved.TryGetValue(reference, out Matrix previous) && previous != ambient)
                {
                    throw new InvalidOperationException(
                        "A scope whose transform is defined against the ambient transform - "
                        + "TransformOperator.Append or TransformOperator.Set - was reached under two different "
                        + "ambient transforms, and one scope resolves to one matrix. Record the subtree once "
                        + "per consumer, or express the scope with TransformOperator.Prepend, which is stated "
                        + "in its input's own space and needs no ambient.");
                }

                resolved[reference] = ambient;
                reference.ApplyResolvedPayload(new TargetScopeRenderFragmentPayload(
                    RenderScopeAmbientTransform.CreateScope(
                        declaration,
                        effective,
                        payload.Description.BuiltInBackdropCapturesBackingTarget)));
            }

            inputAmbient = RenderScopeAmbientTransform.Compose(effective, ambient);
        }

        foreach (RenderFragmentReference input in reference.Inputs)
            Visit(input, inputAmbient, visited, resolved);
    }

    private readonly record struct VisitedAmbient(RenderFragmentReference Reference, Matrix Ambient)
    {
        public bool Equals(VisitedAmbient other)
            => ReferenceEquals(Reference, other.Reference) && Ambient == other.Ambient;

        public override int GetHashCode()
            => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Reference), Ambient);
    }
}
