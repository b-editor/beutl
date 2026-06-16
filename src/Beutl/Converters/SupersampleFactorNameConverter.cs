using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

/// <summary>
/// Maps an export supersampling factor to its display name (feature 003, US4):
/// <c>1</c> → localized "Off", any other factor → "<c>N×</c>". One-way; used from ComboBox item templates.
/// </summary>
public sealed class SupersampleFactorNameConverter : IValueConverter
{
    public static readonly SupersampleFactorNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            1 => Strings.Off,
            int factor => string.Create(culture, $"{factor}x"),
            _ => BindingNotification.Null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
