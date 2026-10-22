using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

public sealed class MediaTimeToSecondsConverter : IValueConverter
{
    public static readonly MediaTimeToSecondsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MediaTime time)
        {
            return time.ToSeconds();
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
            return MediaTime.FromSeconds(secs);
        }
        else
        {
            return BindingNotification.Null;
        }
    }
}
