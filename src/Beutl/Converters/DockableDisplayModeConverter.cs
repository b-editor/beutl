using Avalonia.Data.Converters;
using ReDocking;

namespace Beutl.Converters;

public class DockableDisplayModeConverter : IValueConverter
{
    public static readonly DockableDisplayModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ToolTabExtension.TabDisplayMode mode)
        {
            return mode switch
            {
                ToolTabExtension.TabDisplayMode.Docked => DockableDisplayMode.Docked,
                ToolTabExtension.TabDisplayMode.Floating => DockableDisplayMode.Floating,
                _ => DockableDisplayMode.Docked
            };
        }

        return DockableDisplayMode.Docked;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DockableDisplayMode mode)
        {
            return mode switch
            {
                DockableDisplayMode.Docked => ToolTabExtension.TabDisplayMode.Docked,
                DockableDisplayMode.Floating => ToolTabExtension.TabDisplayMode.Floating,
                _ => ToolTabExtension.TabDisplayMode.Docked
            };
        }

        return ToolTabExtension.TabDisplayMode.Docked;
    }
}
