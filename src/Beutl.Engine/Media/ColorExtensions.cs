namespace Beutl.Media;

public static class ColorExtensions
{
    public static SolidColorBrush ToBrush(this Color color)
    {
        return new SolidColorBrush(color);
    }

    public static SolidColorBrush.Resource ToBrushResource(this Color color)
    {
        return new SolidColorBrush.Resource { Color = color, Opacity = 100f };
    }
}
