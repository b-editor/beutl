namespace Beutl.Graphics.Rendering;

internal enum RenderFragmentKind : byte
{
    ContributeValues,
    Opacity,
    Blend,
    OpacityMask,
    Shader,
    OpaqueSource,
    OpaqueMap,
    OpaqueCombine,
    OpaqueExpand,
    FilterEffectSegment,
    TargetCapture,
    Layer,
    TargetLayerScope,
    TargetScope,
    RawTargetCommand,
    TargetCommand,
    BuiltInBackdropCapture,
}
