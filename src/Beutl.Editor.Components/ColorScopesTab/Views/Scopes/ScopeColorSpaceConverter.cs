using Avalonia.Data.Converters;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

public sealed class ScopeColorSpaceConverter : IValueConverter
{
    public static readonly ScopeColorSpaceConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ScopeColorSpace colorSpace)
        {
            return (int)colorSpace;
        }

        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && Enum.IsDefined(typeof(ScopeColorSpace), index))
        {
            return (ScopeColorSpace)index;
        }

        return ScopeColorSpace.Gamma;
    }
}
