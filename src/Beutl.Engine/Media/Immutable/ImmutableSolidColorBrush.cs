using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableSolidColorBrush(
    Color color, float opacity = 100, ImmutableTransform? transform = null, RelativePoint origin = default)
    : ISolidColorBrush, IEquatable<ISolidColorBrush?>
{
    public ImmutableSolidColorBrush(uint color)
        : this(Color.FromUInt32(color))
    {
    }

    public ImmutableSolidColorBrush(ISolidColorBrush source)
        : this(source.Color, source.Opacity, source.Transform?.ToImmutable())
    {
    }

    public Color Color { get; } = color;

    public float Opacity { get; } = opacity;

    public ITransform? Transform { get; } = transform;

    public RelativePoint TransformOrigin { get; } = origin;

    public override string ToString()
    {
        return Color.ToString();
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ISolidColorBrush);
    }

    public bool Equals(ISolidColorBrush? other)
    {
        return other is not null
            && Color.Equals(other.Color)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Color, Opacity, Transform, TransformOrigin);
    }
}
