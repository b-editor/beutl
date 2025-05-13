using Avalonia.Data.Converters;

namespace Beutl.Converters;

public sealed class EqualAllConverter : IMultiValueConverter
{
    public static readonly EqualAllConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0)
        {
            return false;
        }

        object? firstValue = values[0];
        foreach (object? value in values)
        {
            if (!Equals(firstValue, value))
            {
                return false;
            }
        }

        return true;
    }
}
