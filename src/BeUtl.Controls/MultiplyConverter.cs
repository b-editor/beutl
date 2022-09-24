using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace BeUtl.Controls;

public sealed class MultiplyConverter : IMultiValueConverter
{
    public static readonly MultiplyConverter Instance = new();

    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!values.All(x => x is double or UnsetValueType))
        {
            throw new InvalidOperationException("Multiplication other than double type is not supported.");
        }

        if (!values.Any())
        {
            goto ReturnZero;
        }

        if (values[0] is double value)
        {
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] is double d)
                {
                    value *= d;
                }
                else
                {
                    goto ReturnZero;
                }
            }

            return value;
        }

    ReturnZero:
        return 0.0;
    }
}
