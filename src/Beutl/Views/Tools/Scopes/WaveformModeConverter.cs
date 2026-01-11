using Avalonia.Data.Converters;

namespace Beutl.Views.Tools.Scopes;

public sealed class WaveformModeConverter : IValueConverter
{
    public static readonly WaveformModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is WaveformMode mode)
        {
            return (int)mode;
        }

        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && Enum.IsDefined(typeof(WaveformMode), index))
        {
            return (WaveformMode)index;
        }

        return WaveformMode.Luma;
    }
}
