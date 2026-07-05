namespace Beutl.Graphics.Effects;

/// <summary>
/// The seven concrete descriptor kinds realizing the spec's five primitives (feature 004, research D7).
/// Only <see cref="Shader"/>, <see cref="ColorFilter"/>, <see cref="SkiaFilter"/> and the transition-only
/// <see cref="OpaqueLegacy"/> are producible in rollout step 3a; the remaining kinds land with their
/// descriptors in a later step.
/// </summary>
public enum EffectNodeKind
{
    /// <summary>An SKSL shader node (snippet or whole-source).</summary>
    Shader,

    /// <summary>An <c>SKColorFilter</c> node (always coordinate-invariant).</summary>
    ColorFilter,

    /// <summary>An <c>SKImageFilter</c> node (blur, drop-shadow, morphology, matrix, …).</summary>
    SkiaFilter,

    /// <summary>A Vulkan compute node.</summary>
    Compute,

    /// <summary>An imperative canvas-drawing node.</summary>
    Geometry,

    /// <summary>A fan-out node.</summary>
    Split,

    /// <summary>A fan-in composite node.</summary>
    Composite,

    /// <summary>A transition-only node wrapping an unmigrated effect's legacy item list (deleted with the bridge in step 6).</summary>
    OpaqueLegacy,
}

/// <summary>
/// The immutable base of every effect-graph node descriptor (feature 004, data-model §1). A descriptor carries
/// data — a shader source, a filter factory, a bounds contract — never a rendering callback with target access
/// (the sole exception is the later <c>GeometryNode</c>). The compiler classifies, fuses and schedules
/// descriptors; the executor runs them. Descriptors are constructed in <see cref="FilterEffect.Describe"/> and
/// MUST NOT render or allocate (A1).
/// </summary>
public abstract record EffectNodeDescriptor
{
    /// <summary>Which primitive this descriptor is.</summary>
    public abstract EffectNodeKind Kind { get; }

    /// <summary>This node's forward/backward bounds contract. Identity for coordinate-invariant nodes.</summary>
    public abstract BoundsContract Bounds { get; }

    /// <summary>
    /// True when this node samples only the current output pixel, so it may fuse with adjacent invariant
    /// nodes into one draw. Coordinate-invariant nodes have identity bounds by construction (A3).
    /// </summary>
    public abstract bool IsCoordinateInvariant { get; }
}
