using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;

using BEditor.Models.Extension;

using BEditor.NET.Media;

namespace BEditor.ViewModels.Converters {
    public class ColorToBrush : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is Color4 color) {
                return color.ToBrush();
            }
            else if (value is Color3 color3) {
                return color3.ToBrush();
            }

            return new SolidColorBrush((Color)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
