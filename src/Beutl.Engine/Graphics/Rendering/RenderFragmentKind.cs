namespace Beutl.Graphics.Rendering;

internal enum RenderFragmentKind : byte
{
    ContributeValues,
    Opacity,
    Blend,
    OpacityMask,
    Shader,
    ShaderGraph,
    Geometry,
    OpaqueSource,
    OpaqueMap,
    OpaqueCombine,
    OpaqueExpand,
    FilterEffectSegment,
    MaterializedInput,
    TargetCapture,
    Layer,
    TargetLayerScope,
    TargetScope,
    RawTargetScope,
    RawTargetCommand,
    TargetCommand,
    BuiltInBackdropCapture,
}
