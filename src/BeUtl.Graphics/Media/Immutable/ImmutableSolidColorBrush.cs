namespace BeUtl.Media.Immutable;

public sealed record ImmutableSolidColorBrush(Color Color, float Opacity) : ISolidColorBrush
{
    public override string ToString()
    {
        return Color.ToString();
    }
}
