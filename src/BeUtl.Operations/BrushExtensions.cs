using BeUtl.Media;

namespace BeUtl.Operations;

public static class BrushExtensions
{
    public static Color TryGetColorOrDefault(this IBrush brush, Color defaultValue)
    {
        if (brush is ISolidColorBrush solidBrush)
        {
            return solidBrush.Color;
        }
        else
        {
            return defaultValue;
        }
    }

    public static bool TrySetColor(this IBrush brush, Color value)
    {
        if (brush is SolidColorBrush solidBrush)
        {
            solidBrush.Color = value;
            return true;
        }
        else
        {
            return false;
        }
    }
}
