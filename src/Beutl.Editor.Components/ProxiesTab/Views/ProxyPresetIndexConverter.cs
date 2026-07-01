using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Beutl.Media.Proxy;

namespace Beutl.Editor.Components.ProxiesTab.Views;

public sealed class ProxyPresetIndexConverter : IValueConverter
{
    public static readonly ProxyPresetIndexConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ProxyPreset.Half => 0,
            ProxyPreset.Quarter => 1,
            ProxyPreset.Eighth => 2,
            _ => 1,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            0 => ProxyPreset.Half,
            1 => ProxyPreset.Quarter,
            2 => ProxyPreset.Eighth,
            _ => BindingOperations.DoNothing,
        };
    }
}
