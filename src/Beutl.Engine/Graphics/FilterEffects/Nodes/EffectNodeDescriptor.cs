namespace Beutl.Graphics.Effects;

/// <summary>
/// The immutable base of every effect-graph node descriptor (feature 004, data-model §1). A descriptor carries
/// data — a shader source, a filter factory, a bounds contract — never a rendering callback with target access
/// (the sole exception is the later <c>GeometryNode</c>). The compiler classifies, fuses and schedules
/// descriptors by their concrete type; the executor runs them. Descriptors are constructed in
/// <see cref="FilterEffect.Describe"/> and MUST NOT render or allocate (A1).
/// </summary>
public abstract record EffectNodeDescriptor
{
    /// <summary>This node's forward/backward bounds contract. Identity for coordinate-invariant nodes.</summary>
    public abstract BoundsContract Bounds { get; }

    /// <summary>
    /// True when this node samples only the current output pixel, so it may fuse with adjacent invariant
    /// nodes into one draw. Coordinate-invariant nodes have identity bounds by construction (A3).
    /// </summary>
    public abstract bool IsCoordinateInvariant { get; }
}
