using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Editor.Components.Converters;

public sealed class AvaloniaThicknessConverter : IValueConverter
{
    public static readonly AvaloniaThicknessConverter Instance = new();

    // Makes a left margin; the only use passes ConverterParameter='Left'.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is double left ? new Thickness(left, 0, 0, 0) : default(Thickness);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(value);
    }
}
