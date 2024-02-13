using Beutl.Media.Immutable;

namespace Beutl.Media;

public static class ColorExtensions
{
    public static ISolidColorBrush ToBrush(this Color color)
    {
        return new SolidColorBrush(color);
    }
    
    public static ISolidColorBrush ToImmutableBrush(this Color color)
    {
        return new ImmutableSolidColorBrush(color, 100);
    }
}
