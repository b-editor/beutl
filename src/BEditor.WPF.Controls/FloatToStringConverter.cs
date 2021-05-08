using System;
using System.Globalization;
using System.Windows.Data;

namespace BEditor.WPF.Controls
{
    public class FloatToStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string val && float.TryParse(val, out var v)) return v;

            return 0;
        }
    }
    public class ByteToStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string val && byte.TryParse(val, out var v)) return v;

            return 0;
        }
    }
}