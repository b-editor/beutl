namespace Beutl.Graphics.Rendering.Requests;

internal static class RenderMaterializationDensityPolicy
{
    /// <param name="regions">What the request needs of each fragment, which limits what a shader graph allocates.</param>
    public static float Clamp(
        RenderFragmentReference fragment,
        float density,
        RegionAnalysis? regions = null)
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
            return Clamp(fragment.Inputs[0], density, regions);
        }

        if (fragment.Payload is ShaderGraphRenderFragmentPayload graph)
        {
            return graph.ClampDensity(fragment.Inputs[0], density, regions?.GetFragmentRequirement(fragment)
                .Resolve(fragment.Bounds).Intersect(fragment.Bounds) ?? Rect.Empty);
        }

        Rect logicalBounds = fragment.Kind == RenderFragmentKind.Layer
                             && fragment.Payload is LayerRenderFragmentPayload layer
            ? layer.Domain ?? fragment.Bounds
            // A segment makes only the part of its output the request needs.
            : fragment.Payload is FilterEffectSegmentRenderFragmentPayload && regions is not null
                ? regions.GetFragmentRequirement(fragment).Resolve(fragment.Bounds).Intersect(fragment.Bounds)
                : fragment.Bounds;
        return RequiresRasterApron(fragment)
            ? BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(logicalBounds, density)
            : BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(logicalBounds, density);
    }

    public static bool RequiresRasterApron(RenderFragmentReference fragment)
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
