using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Converters;

/// <summary>
/// Maps a neighbour's visibility onto a grid span so a paired field can take the whole
/// row once its partner is hidden. The parameter is the row's column count (default 2).
/// </summary>
public sealed class GridSpanConverter : IValueConverter
{
    public static readonly GridSpanConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 1 : ParseColumnCount(parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }

    private static int ParseColumnCount(object? parameter)
    {
        return parameter switch
        {
            int count when count > 0 => count,
            string text when int.TryParse(text, CultureInfo.InvariantCulture, out int count) && count > 0 => count,
            _ => 2,
        };
    }
}
