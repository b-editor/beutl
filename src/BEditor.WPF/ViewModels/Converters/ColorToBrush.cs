using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using BEditor.Models.Extension;

namespace BEditor.ViewModels.Converters
{
    public sealed class ColorToBrush : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Drawing.Color color)
            {
                return color.ToBrush();
            }

            return new SolidColorBrush((Color)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

}