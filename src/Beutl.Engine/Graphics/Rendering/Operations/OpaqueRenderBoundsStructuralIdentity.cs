namespace Beutl.Graphics.Rendering;

internal readonly record struct OpaqueRenderBoundsStructuralIdentity(
    OpaqueRenderBoundsKind Kind,
    object? ForwardIdentity,
    object? BackwardIdentity,
    object? ExplicitKey);
