using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.Helpers;

public static class FrameNumberHelper
{
    public static readonly double SecondWidth;
    public static readonly double LayerHeight;

    static FrameNumberHelper()
    {
        SecondWidth = ResolveDouble("SecondWidth", 150d);
        LayerHeight = ResolveDouble("LayerHeight", 25d);
    }

    // Styles.axaml defines both keys as x:Double, and no theme redefines them.
    private static double ResolveDouble(string key, double fallback)
        => Application.Current?.FindResource(key) is double value ? value : fallback;

    public static double TimeToPixel(this MediaTime time, float scale)
    {
        return time.ToSeconds() * SecondWidth * scale;
    }

    public static MediaTime PixelToTime(this double pixel, float scale)
    {
        return MediaTime.FromSeconds(pixel / (SecondWidth * scale));
    }
}
