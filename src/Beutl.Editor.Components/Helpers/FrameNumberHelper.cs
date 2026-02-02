using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.Helpers;

public static class FrameNumberHelper
{
    public static readonly double SecondWidth;
    public static readonly double LayerHeight;

    static FrameNumberHelper()
    {
        SecondWidth = (double)(Application.Current?.FindResource("SecondWidth") ?? 150);
        LayerHeight = (double)(Application.Current?.FindResource("LayerHeight") ?? 25);
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

    public static double ToPixel(this TimeSpan time)
    {
        return time.TotalSeconds * SecondWidth;
    }

    public static TimeSpan ToTimeSpan(this double pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondWidth);
    }

    public static TimeSpan ToTimeSpanF(this float pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondWidth);
    }

    public static double ToPixel(this TimeSpan time, float scale)
    {
        return time.TotalSeconds * SecondWidth * scale;
    }

    public static float ToPixelF(this TimeSpan time, float scale)
    {
        return (float)(time.TotalSeconds * SecondWidth * scale);
    }

    public static TimeSpan ToTimeSpan(this double pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondWidth * scale));
    }

    public static TimeSpan ToTimeSpanF(this float pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondWidth * scale));
    }
}
