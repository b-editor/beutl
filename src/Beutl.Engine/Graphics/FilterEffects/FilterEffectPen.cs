using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An authoring-time handle to a pen an effect strokes with inside an execution-time callback.
/// </summary>
/// <remarks>
/// Obtain a handle from <see cref="FilterEffectContext.RegisterPen"/> while recording and stroke with it from a
/// custom-effect callback through <see cref="CustomFilterEffectContext.DrawPath"/>. Registration lowers the pen's
/// nested <see cref="DrawableBrush"/> content into the recorded render graph.
/// </remarks>
public sealed class FilterEffectPen : IEquatable<FilterEffectPen>
{
    internal FilterEffectPen(Pen.Resource? resource, FilterEffectBrush brush)
    {
        Resource = resource;
        Brush = brush;
    }

    /// <summary>Gets the handle of an absent pen. Stroking with it is a no-op.</summary>
    public static FilterEffectPen Empty { get; } = new(null, FilterEffectBrush.Empty);

    /// <summary>Gets whether this handle refers to no pen at all.</summary>
    public bool IsEmpty => Resource is null;

    internal Pen.Resource? Resource { get; }

    internal FilterEffectBrush Brush { get; }

    public bool Equals(FilterEffectPen? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null)
            return false;

        return ReferenceEquals(Resource, other.Resource) && Brush.Equals(other.Brush);
    }

    public override bool Equals(object? obj) => Equals(obj as FilterEffectPen);

    public override int GetHashCode() => HashCode.Combine(Resource, Brush);
}
