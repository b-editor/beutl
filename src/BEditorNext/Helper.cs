using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

namespace BEditorNext;

internal static class Helper
{
    public const double SecondPixels = 150;
    public const double ClipHeight = 25;

    public static double ToPixel(this TimeSpan time)
    {
        return time.TotalSeconds * SecondPixels;
    }

    public static TimeSpan ToTimeSpan(this double pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondPixels);
    }

    public static double ToPixel(this TimeSpan time, float scale)
    {
        return time.TotalSeconds * SecondPixels * scale;
    }

    public static TimeSpan ToTimeSpan(this double pixel, float scale)
    {
        return TimeSpan.FromSeconds(pixel / (SecondPixels * scale));
    }

    public static int ToLayerNumber(this double pixel)
    {
        return (int)(pixel / ClipHeight);
    }

    public static double ToLayerPixel(this int layer)
    {
        return layer * ClipHeight;
    }

    public static T FindResourceOrDefault<T>(this ResourceReference<T> reference, T @default)
    {
        return (T?)Application.Current.FindResource(reference.Key) ?? @default;
    }
}
