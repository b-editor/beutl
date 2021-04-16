using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Data.Converters;

using BEditor.Media;

namespace BEditor.Converters
{
    public class FrameToDoubleConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Frame f) return f.Value;

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double f) return new Frame((int)f);

            return null;
        }
    }
}