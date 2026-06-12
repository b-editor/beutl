using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

/// <summary>
/// Maps a save-frame output-resolution multiplier to its display name (feature 003, US4 follow-up):
/// any factor → "<c>N×</c>" (e.g. <c>0.5×</c>, <c>1×</c>, <c>2×</c>). One-way; used from ComboBox item
/// templates. Unlike <see cref="SupersampleFactorNameConverter"/>, <c>1</c> is shown as "1×" (not "Off")
/// because the save scale is an output resolution, not a quality toggle.
/// </summary>
public sealed class SaveFrameScaleNameConverter : IValueConverter
{
    public static readonly SaveFrameScaleNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            float scale => string.Create(culture, $"{scale:0.##}×"),
            _ => BindingNotification.Null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
