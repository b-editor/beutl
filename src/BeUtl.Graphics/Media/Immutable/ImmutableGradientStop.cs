namespace BeUtl.Media.Immutable;

public class ImmutableGradientStop : IGradientStop
{
    public ImmutableGradientStop(float offset, Color color)
    {
        Offset = offset;
        Color = color;
    }

    public float Offset { get; }

    public Color Color { get; }
}
