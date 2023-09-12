using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

public sealed class TimeSpanToDoubleConverter : IValueConverter
{
    public static readonly TimeSpanToDoubleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            return ts.TotalSeconds;
        }
        else
        {
            return BindingNotification.Null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double secs)
        {
            return TimeSpan.FromSeconds(secs);
        }
        else
        {
            return BindingNotification.Null;
        }
    }
}
