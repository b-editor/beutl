namespace Beutl.Media.Immutable;

public class ImmutableGradientStop : IGradientStop, IEquatable<IGradientStop?>
{
    public ImmutableGradientStop(float offset, Color color)
    {
        Offset = offset;
        Color = color;
    }

    public float Offset { get; }

    public Color Color { get; }

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
