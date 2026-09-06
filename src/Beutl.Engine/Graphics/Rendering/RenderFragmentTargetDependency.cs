namespace Beutl.Graphics.Rendering;

internal static class RenderFragmentTargetDependency
{
    public static bool HasExternalTargetDependency(RenderFragmentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        return Visit(reference, visited);
    }

    private static bool Visit(
        RenderFragmentReference reference,
        ISet<RenderFragmentReference> visited)
    {
        if (!visited.Add(reference))
            return false;

        if (reference.Kind == RenderFragmentKind.Layer)
        {
            // A finite Layer owns a fresh transparent target. Target operations below it are
            // self-contained inputs to the resulting value, not dependencies on the caller's target token.
            return false;
        }

        if (reference.Kind is RenderFragmentKind.TargetCapture
            or RenderFragmentKind.BuiltInBackdropCapture
            or RenderFragmentKind.TargetCommand
            or RenderFragmentKind.RawTargetCommand
            or RenderFragmentKind.TargetLayerScope
            or RenderFragmentKind.RawTargetScope)
        {
            return true;
        }

        if (reference.Kind == RenderFragmentKind.TargetScope
            && ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.IsValueReplayMap is false)
        {
            return true;
        }

        for (int index = 0; index < reference.Inputs.Length; index++)
        {
            if (Visit(reference.Inputs[index], visited))
                return true;
        }

        return false;
    }
}
