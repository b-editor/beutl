using Avalonia.Data;
using Avalonia.Data.Converters;

using Beutl.Models;

namespace Beutl.Editor.Components.Converters;

/// <summary>
/// Localized display names for <see cref="RenderScale"/>. One-way.
/// </summary>
public sealed class RenderScaleNameConverter : IValueConverter
{
    public static readonly RenderScaleNameConverter Instance = new();

    public static string GetName(RenderScale scale) => scale switch
    {
        RenderScale.Full => Strings.RenderScale_Full,
        RenderScale.Half => Strings.RenderScale_Half,
        RenderScale.Quarter => Strings.RenderScale_Quarter,
        RenderScale.FitToPreviewer => Strings.RenderScale_FitToPreviewer,
        _ => scale.ToString(),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is RenderScale scale ? GetName(scale) : BindingNotification.Null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
