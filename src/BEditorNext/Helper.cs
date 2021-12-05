using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;

namespace BEditorNext;

internal static class Helper
{
    public static readonly double SecondWidth;
    public static readonly double LayerHeight;

    static Helper()
    {
        SecondWidth = (double)(Application.Current.FindResource("SecondWidth") ?? 150);
        LayerHeight = (double)(Application.Current.FindResource("LayerHeight") ?? 25);
    }

    public static Color ToAvalonia(this in Media.Color color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static double ToPixel(this TimeSpan time)
    {
        return time.TotalSeconds * SecondWidth;
    }

    public static TimeSpan ToTimeSpan(this double pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondWidth);
    }

    public static double ToPixel(this TimeSpan time, float scale)
    {
        return time.TotalSeconds * SecondWidth * scale;
    }

    public static TimeSpan ToTimeSpan(this double pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondWidth * scale));
    }

    public static int ToLayerNumber(this double pixel)
    {
        return (int)Math.Floor(pixel / LayerHeight);
    }

    public static int ToLayerNumber(this Thickness thickness)
    {
        return (int)Math.Floor((thickness.Top + (LayerHeight / 2)) / LayerHeight);
    }

    public static double ToLayerPixel(this int layer)
    {
        return layer * LayerHeight;
    }

    public static T FindResourceOrDefault<T>(this ResourceReference<T> reference, T @default)
    {
        return (T?)Application.Current.FindResource(reference.Key) ?? @default;
    }
}
