namespace Beutl.Graphics.Effects;

/// <summary>
/// The immutable base of every effect-graph node descriptor (feature 004, data-model §1). A descriptor carries
/// data — a shader source, a filter factory, a bounds contract — never a rendering callback with target access
/// (the sole exception is the later <c>GeometryNode</c>). The compiler classifies, fuses and schedules
/// descriptors by their concrete type; the executor runs them. The descriptor vocabulary is a closed union:
/// plugin authors compose the public sealed descriptor types rather than deriving an unknown compiler node.
/// Descriptors are constructed in <see cref="FilterEffect.Describe"/> and MUST NOT render or allocate (A1).
/// </summary>
public abstract record EffectNodeDescriptor
{
    private protected EffectNodeDescriptor()
    {
    }

    // A non-public abstract discriminator makes this a closed union even though C# requires a non-sealed record's
    // synthesized copy constructor to be protected. Code outside Beutl.Engine cannot implement this member.
    internal abstract EffectNodeKind Kind { get; }

    /// <summary>This node's forward/backward bounds contract. Identity for coordinate-invariant nodes.</summary>
    public abstract BoundsContract Bounds { get; }

    /// <summary>
    /// True when this node samples only the current output pixel, so it may fuse with adjacent invariant
    /// nodes into one draw. Coordinate-invariant nodes have identity bounds by construction (A3).
    /// </summary>
    public abstract bool IsCoordinateInvariant { get; }
}

internal enum EffectNodeKind
{
    Shader,
    ColorFilter,
    SkiaFilter,
    Geometry,
    Compute,
    Split,
    Composite,
    NestedGraph,
    CustomRenderNode,
}
