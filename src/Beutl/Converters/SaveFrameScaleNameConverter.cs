using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

/// <summary>Converts a save-frame scale multiplier to a display string like "2x".</summary>
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
