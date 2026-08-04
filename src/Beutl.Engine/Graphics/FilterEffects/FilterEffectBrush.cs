using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An authoring-time handle to a brush an effect paints with inside an execution-time callback.
/// </summary>
/// <remarks>
/// Obtain a handle from <see cref="FilterEffectContext.RegisterBrush"/> while recording and paint with it from a
/// custom-effect callback through <see cref="CustomFilterEffectContext.CreateBrushShader"/> or
/// <see cref="CustomFilterEffectContext.ConfigureBrushPaint"/>. Registration is what lowers nested
/// <see cref="DrawableBrush"/> content into the recorded render graph; a brush captured directly into a callback
/// stays opaque to the planner and cannot resolve its nested content.
/// </remarks>
public sealed class FilterEffectBrush : IEquatable<FilterEffectBrush>
{
    internal FilterEffectBrush(Brush.Resource? resource, object? identity)
    {
        Resource = resource;
        Identity = identity;
    }

    /// <summary>Gets the handle of an absent brush. Painting with it is a no-op.</summary>
    public static FilterEffectBrush Empty { get; } = new(null, null);

    /// <summary>Gets whether this handle refers to no brush at all.</summary>
    public bool IsEmpty => Resource is null;

    internal Brush.Resource? Resource { get; }

    /// <summary>
    /// The recording-stable identity of the referenced brush. Two handles for the same brush compare equal across
    /// frames, so callback data embedding a handle keeps a stable structural identity.
    /// </summary>
    internal object? Identity { get; }

    public bool Equals(FilterEffectBrush? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null)
            return false;

        return Equals(Identity, other.Identity);
    }

    public override bool Equals(object? obj) => Equals(obj as FilterEffectBrush);

    public override int GetHashCode() => Identity?.GetHashCode() ?? 0;
}
