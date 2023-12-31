namespace Beutl.Media.Immutable;

public class ImmutableGradientStop(float offset, Color color) : IGradientStop, IEquatable<IGradientStop?>
{
    public float Offset { get; } = offset;

    public Color Color { get; } = color;

    public override bool Equals(object? obj)
    {
        return Equals(obj as IGradientStop);
    }

    public bool Equals(IGradientStop? other)
    {
        return other is not null && Offset == other.Offset && Color.Equals(other.Color);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Offset, Color);
    }
}
