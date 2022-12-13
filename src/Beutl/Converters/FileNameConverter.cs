using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

public sealed class FileNameConverter : IValueConverter
{
    public static readonly FileNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return Path.GetFileName(str);
        }
        else
        {
            return new BindingNotification(value);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(value);
    }
}
