using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using BeUtl.ProjectSystem;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Styling;

namespace BeUtl;

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

    public static IObservable<T> ToObservable<T>(this ResourceReference<T> resourceReference, T defaultValue)
        where T : class
    {
        if (resourceReference.Key != null)
        {
            return Application.Current!.GetResourceObservable(resourceReference.Key)
                .Select(i => (i as T) ?? defaultValue);
        }
        else
        {
            return Observable.Return(defaultValue);
        }
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

    public static string RandomLayerFileName(string baseDir, string ext)
    {
        string filename = Path.Combine(baseDir, $"{RandomString()}.{ext}");
        while (File.Exists(filename))
        {
            filename = Path.Combine(baseDir, $"{RandomString()}.{ext}");
        }

        return filename;
    }

    public static T? GetMaximumOrDefault<T>(this IWrappedProperty wrappedProp, T defaultValue, Type? type = null)
    {
        OperationPropertyMetadata<T>? metadata
            = wrappedProp.GetMetadataExt<OperationPropertyMetadata<T>>(type);
        return metadata?.HasMaximum == true ? metadata.Maximum : defaultValue;
    }

    public static T? GetMinimumOrDefault<T>(this IWrappedProperty wrappedProp, T defaultValue, Type? type = null)
    {
        OperationPropertyMetadata<T>? metadata
            = wrappedProp.GetMetadataExt<OperationPropertyMetadata<T>>(type);
        return metadata?.HasMinimum == true ? metadata.Minimum : defaultValue;
    }

    public static object? GetDefaultValue(this IWrappedProperty wrappedProp, Type? type = null)
    {
        return wrappedProp.GetMetadataExt<ICorePropertyMetadata>(type)?.GetDefaultValue();
    }

    public static TMetadata? GetMetadataExt<TMetadata>(this IWrappedProperty wrappedProp, Type? type = null)
        where TMetadata : ICorePropertyMetadata
    {
        TMetadata? result = default;
        if (type != null)
        {
            wrappedProp.AssociatedProperty.TryGetMetadata(type, out result);
        }
        else
        {
            switch (wrappedProp.Tag)
            {
                case CoreObject obj:
                    wrappedProp.AssociatedProperty.TryGetMetadata(obj.GetType(), out result);
                    break;
                case IPropertyInstance pi:
                    wrappedProp.AssociatedProperty.TryGetMetadata(pi.Parent.GetType(), out result);
                    break;
                default:
                    wrappedProp.AssociatedProperty.TryGetMetadata(wrappedProp.AssociatedProperty.OwnerType, out result);
                    break;
            }
        }

        return result;
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
