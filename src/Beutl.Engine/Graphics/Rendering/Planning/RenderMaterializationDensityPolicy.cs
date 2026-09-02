namespace Beutl.Graphics.Rendering;

internal static class RenderMaterializationDensityPolicy
{
    public static float Clamp(
        RenderFragmentReference fragment,
        float density)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Kind is RenderFragmentKind.MaterializedInput
            or RenderFragmentKind.BuiltInBackdropCapture)
        {
            return density;
        }
        if (fragment.Kind == RenderFragmentKind.ContributeValues
            && fragment.Inputs.Length == 1)
        {
            return Clamp(fragment.Inputs[0], density);
        }

        Rect logicalBounds = fragment.Kind == RenderFragmentKind.Layer
                             && fragment.Payload is LayerRenderFragmentPayload layer
            ? layer.Domain ?? fragment.Bounds
            : fragment.Bounds;
        return RequiresRasterApron(fragment)
            ? RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(logicalBounds, density)
            : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(logicalBounds, density);
    }

    private static bool RequiresRasterApron(RenderFragmentReference fragment)
    {
        if (fragment.Kind == RenderFragmentKind.OpaqueSource
            && fragment.Payload is OpaqueRenderFragmentPayload opaque)
        {
            return opaque.Description.HasDirectReplayMaterializationContract;
        }

        return fragment.Kind == RenderFragmentKind.TargetScope
               && fragment.Payload is TargetScopeRenderFragmentPayload targetScope
               && targetScope.Description.IsValueReplayMap;
    }
}
