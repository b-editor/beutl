using System.Globalization;
using Avalonia.Data.Converters;

namespace Beutl.Views.Tools.Scopes;

public enum HistogramMode
{
    Overlay = 0,
    Parade = 1
}

public sealed class HistogramModeConverter : IValueConverter
{
    public static readonly HistogramModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is HistogramMode mode)
        {
            return (int)mode;
        }

        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && Enum.IsDefined(typeof(HistogramMode), index))
        {
            return (HistogramMode)index;
        }

        return HistogramMode.Overlay;
    }
}
