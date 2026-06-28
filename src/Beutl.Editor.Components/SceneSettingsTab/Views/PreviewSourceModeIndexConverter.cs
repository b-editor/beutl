using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Beutl.Media.Proxy;

namespace Beutl.Editor.Components.SceneSettingsTab.Views;

public sealed class PreviewSourceModeIndexConverter : IValueConverter
{
    public static readonly PreviewSourceModeIndexConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PreviewSourceMode.PreferProxy => 0,
            PreviewSourceMode.ForceOriginal => 1,
            _ => 0,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            0 => PreviewSourceMode.PreferProxy,
            1 => PreviewSourceMode.ForceOriginal,
            _ => BindingOperations.DoNothing,
        };
    }
}
