using Avalonia.Data.Converters;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

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
