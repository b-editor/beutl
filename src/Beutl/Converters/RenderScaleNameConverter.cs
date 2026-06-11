using Avalonia.Data;
using Avalonia.Data.Converters;
using Beutl.Models;

namespace Beutl.Converters;

/// <summary>
/// Maps <see cref="RenderScale"/> values to their localized display names for the preview
/// render-quality selector (feature 003, US4). One-way; used from ComboBox item templates.
/// </summary>
public sealed class RenderScaleNameConverter : IValueConverter
{
    public static readonly RenderScaleNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            RenderScale.Full => Strings.RenderScale_Full,
            RenderScale.Half => Strings.RenderScale_Half,
            RenderScale.Quarter => Strings.RenderScale_Quarter,
            RenderScale.FitToPreviewer => Strings.RenderScale_FitToPreviewer,
            RenderScale other => other.ToString(),
            _ => BindingNotification.Null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
