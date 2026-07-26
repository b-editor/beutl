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

    // FindResource boxes the resource as its declared type, so an int-typed resource (or a
    // boxed int fallback) would make a direct (double) unbox throw.
    private static double ResolveDouble(string key, double fallback)
    {
        return Application.Current?.FindResource(key) switch
        {
            double d => d,
            int i => i,
            _ => fallback,
        };
    }

    public static int GetFrameRate(this Project? project)
    {
        return project?.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value) == true
            && int.TryParse(value, out int rate)
            ? rate
            : 30;
    }

    public static int GetSampleRate(this Project? project)
    {
        return project?.Variables.TryGetValue(ProjectVariableKeys.SampleRate, out string? value) == true
            && int.TryParse(value, out int rate)
            ? rate
            : 44100;
    }

    public static double TimeToPixel(this TimeSpan time)
    {
        return time.TotalSeconds * SecondWidth;
    }

    public static TimeSpan PixelToTimeSpan(this double pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondWidth);
    }

    public static TimeSpan PixelToTimeSpanF(this float pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondWidth);
    }

    public static double TimeToPixel(this TimeSpan time, float scale)
    {
        return time.TotalSeconds * SecondWidth * scale;
    }

    public static float TimeToPixelF(this TimeSpan time, float scale)
    {
        return (float)(time.TotalSeconds * SecondWidth * scale);
    }

    public static TimeSpan PixelToTimeSpan(this double pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondWidth * scale));
    }

    public static TimeSpan PixelToTimeSpanF(this float pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondWidth * scale));
    }
}
