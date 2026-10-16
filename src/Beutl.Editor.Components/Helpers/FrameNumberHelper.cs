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

    public static double TimeToPixel(this TimeSpan time, float scale)
    {
        return time.TotalSeconds * SecondWidth * scale;
    }

    public static TimeSpan PixelToTimeSpan(this double pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondWidth * scale));
    }
}
