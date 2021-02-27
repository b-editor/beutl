using System;
using System.Globalization;
using System.Windows.Data;

namespace BEditor.ViewModels.Converters
{
    public sealed class MultiBindingToTupple2 : IMultiValueConverter
    {
        object IMultiValueConverter.Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return (values[0], values[1]);
        }

        object[] IMultiValueConverter.ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {

            (object, object) tupple = ((object, object))value;

            return new object[] { tupple.Item1, tupple.Item2 };
        }
    }
}
