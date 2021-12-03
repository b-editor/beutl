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
    public const double SecondPixels = 225;

    public static double ToPixel(this TimeSpan time)
    {
        return time.TotalSeconds * SecondPixels;
    }

    public static TimeSpan ToTimeSpan(this double pixel)
    {
        return TimeSpan.FromSeconds(pixel / SecondPixels);
    }

    public static T FindResourceOrDefault<T>(this ResourceReference<T> reference, T @default)
    {
        return (T?)Application.Current.FindResource(reference.Key) ?? @default;
    }
}
