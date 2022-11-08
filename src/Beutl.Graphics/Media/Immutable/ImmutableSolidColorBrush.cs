using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableSolidColorBrush : ISolidColorBrush, IEquatable<ImmutableSolidColorBrush?>
{
    public ImmutableSolidColorBrush(Color color, float opacity = 1, ImmutableTransform? transform = null, RelativePoint origin = default)
    {
        Color = color;
        Opacity = opacity;
        Transform = transform;
        TransformOrigin = origin;
    }

    public ImmutableSolidColorBrush(uint color)
        : this(Color.FromUInt32(color))
    {
    }

    public ImmutableSolidColorBrush(ISolidColorBrush source)
        : this(source.Color, source.Opacity, source.Transform?.ToImmutable())
    {
    }

    public Color Color { get; }

    public float Opacity { get; }

    public ITransform? Transform { get; }

    public RelativePoint TransformOrigin { get; }

    public override string ToString()
    {
        return Color.ToString();
    }

    public override bool Equals(object? obj) => Equals(obj as ImmutableSolidColorBrush);

    public bool Equals(ImmutableSolidColorBrush? other) => other is not null && Color.Equals(other.Color) && Opacity == other.Opacity && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform) && TransformOrigin.Equals(other.TransformOrigin);

    public override int GetHashCode() => HashCode.Combine(Color, Opacity, Transform, TransformOrigin);

    public static bool operator ==(ImmutableSolidColorBrush? left, ImmutableSolidColorBrush? right) => EqualityComparer<ImmutableSolidColorBrush>.Default.Equals(left, right);

    public static bool operator !=(ImmutableSolidColorBrush? left, ImmutableSolidColorBrush? right) => !(left == right);
}
