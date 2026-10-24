namespace Beutl.Graphics.Rendering;

/// <summary>
/// Answers, for every <see cref="RenderFragmentKind"/>, the device pixel grid a fragment replays its inputs
/// onto.
/// </summary>
/// <remarks>
/// Raw target scopes and commands answer <see cref="RenderDeviceGridMapping.Remapped"/>: the planner cannot analyse
/// their target state, so they cost upstream cache reuse instead of serving a raster at the wrong grid phase.
/// </remarks>
internal static class RenderFragmentDeviceGrid
{
    public static RenderDeviceGridMapping ResolveMapping(RenderFragmentReference reference)
        => reference.Kind switch
        {
            RenderFragmentKind.TargetScope
                => ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.DeviceGridMapping,
            // Every kind whose replay, composition, or value materialization is engine-owned and free of
            // author-supplied target state.
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.Shader
                or RenderFragmentKind.ShaderGraph
                or RenderFragmentKind.Geometry
                or RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                or RenderFragmentKind.FilterEffectSegment
                or RenderFragmentKind.MaterializedInput
                or RenderFragmentKind.TargetCapture
                or RenderFragmentKind.Layer
                or RenderFragmentKind.TargetLayerScope
                or RenderFragmentKind.TargetCommand
                or RenderFragmentKind.BuiltInBackdropCapture
                => RenderDeviceGridMapping.Preserved,
            RenderFragmentKind.RawTargetScope
                or RenderFragmentKind.RawTargetCommand
                => RenderDeviceGridMapping.Remapped,
            _ => throw new InvalidOperationException(
                $"Fragment kind '{reference.Kind}' has no device-grid rule."),
        };
}
