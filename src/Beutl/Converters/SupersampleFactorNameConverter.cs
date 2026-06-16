using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

/// <summary>Converts a supersampling factor to a display name: 1 becomes "Off", others become "Nx".</summary>
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
