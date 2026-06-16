using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

/// <summary>
/// One-way ComboBox-template converter mapping a save-frame output-resolution multiplier to "<c>N×</c>".
/// Unlike <see cref="SupersampleFactorNameConverter"/>, <c>1</c> shows as "1×" not "Off", since this is an
/// output resolution, not a quality toggle.
/// </summary>
public sealed class SaveFrameScaleNameConverter : IValueConverter
{
    public static readonly SaveFrameScaleNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            float scale => string.Create(culture, $"{scale:0.##}x"),
            _ => BindingNotification.Null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
