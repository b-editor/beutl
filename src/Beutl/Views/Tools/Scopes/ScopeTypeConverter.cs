using System.Globalization;
using Avalonia.Data.Converters;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools.Scopes;

/// <summary>
/// Converter for ColorScopeType enum to int (for ComboBox SelectedIndex) and to bool (for visibility).
/// </summary>
public sealed class ScopeTypeConverter : IValueConverter
{
    public static readonly ScopeTypeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ColorScopeType scopeType)
        {
            // If parameter is provided, check for equality (for visibility binding)
            if (parameter is string paramStr && Enum.TryParse<ColorScopeType>(paramStr, out var targetType2))
            {
                return scopeType == targetType2;
            }

            // Otherwise return index for ComboBox
            return (int)scopeType;
        }

        return parameter != null ? false : 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && Enum.IsDefined(typeof(ColorScopeType), index))
        {
            return (ColorScopeType)index;
        }

        return ColorScopeType.Waveform;
    }
}
