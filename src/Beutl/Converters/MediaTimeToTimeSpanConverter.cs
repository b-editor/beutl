using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

// Lets a binding format a time with a TimeSpan format string.
internal sealed class MediaTimeToTimeSpanConverter : IValueConverter
{
    public static readonly MediaTimeToTimeSpanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MediaTime time ? time.ToTimeSpan() : BindingNotification.Null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TimeSpan span ? MediaTime.FromTimeSpan(span) : BindingNotification.Null;
    }
}
