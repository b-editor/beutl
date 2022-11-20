
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Services.Editors.Wrappers;
using Beutl.Streaming;
using Beutl.Styling;

namespace Beutl;

internal static class Helper
{
    public static readonly double SecondWidth;
    public static readonly double LayerHeight;

    static Helper()
    {
        SecondWidth = (double)(Application.Current?.FindResource("SecondWidth") ?? 150);
        LayerHeight = (double)(Application.Current?.FindResource("LayerHeight") ?? 25);
    }

    public static int GetFrameRate(this IWorkspace workspace)
    {
        return workspace.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value)
            && int.TryParse(value, out int rate)
            ? rate
            : 30;
    }

    public static int GetSampleRate(this IWorkspace workspace)
    {
        return workspace.Variables.TryGetValue(ProjectVariableKeys.SampleRate, out string? value)
            && int.TryParse(value, out int rate)
            ? rate
            : 44100;
    }

    public static Color ToAvalonia(this in Media.Color color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Media.Color ToMedia(this in Color color)
    {
        return Media.Color.FromArgb(color.A, color.R, color.G, color.B);
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

    public static string RandomLayerFileName(string baseDir, string ext)
    {
        string filename = Path.Combine(baseDir, $"{RandomString()}.{ext}");
        while (File.Exists(filename))
        {
            filename = Path.Combine(baseDir, $"{RandomString()}.{ext}");
        }

        return filename;
    }

    public static void OrderedAdd<T, TKey>(this IList<T> list, T value, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        comparer ??= Comparer<TKey>.Default;

        TKey? valueKey = keySelector(value);
        for (int i = 0; i < list.Count; i++)
        {
            TKey key = keySelector(list[i]);

            if (comparer.Compare(valueKey, key) <= 0)
            {
                list.Insert(i, value);
                return;
            }
        }

        list.Add(value);
    }
    
    public static void OrderedAddDescending<T, TKey>(this IList<T> list, T value, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        comparer ??= Comparer<TKey>.Default;

        TKey? valueKey = keySelector(value);
        for (int i = 0; i < list.Count; i++)
        {
            TKey key = keySelector(list[i]);

            if (comparer.Compare(valueKey, key) >= 0)
            {
                list.Insert(i, value);
                return;
            }
        }

        list.Add(value);
    }

    private static string RandomString()
    {
        const string characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> Charsarr = stackalloc char[8];
        var random = new Random();

        for (int i = 0; i < Charsarr.Length; i++)
        {
            Charsarr[i] = characters[random.Next(characters.Length)];
        }

        return new string(Charsarr);
    }
}
