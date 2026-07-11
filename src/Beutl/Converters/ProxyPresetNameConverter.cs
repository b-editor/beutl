using Avalonia.Data;
using Avalonia.Data.Converters;

using Beutl.Media.Proxy;

namespace Beutl.Converters;

/// <summary>Converts a <see cref="ProxyPreset"/> to its localized display name.</summary>
public sealed class ProxyPresetNameConverter : IValueConverter
{
    public static readonly ProxyPresetNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ProxyPreset.Half => Strings.ProxyPresetHalf,
            ProxyPreset.Quarter => Strings.ProxyPresetQuarter,
            ProxyPreset.Eighth => Strings.ProxyPresetEighth,
            ProxyPreset preset => preset.ToString(),
            _ => BindingNotification.Null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
