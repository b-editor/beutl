using System;
using System.Globalization;
using System.Windows.Data;

using BEditor.Media;

namespace BEditor.ViewModels.Converters
{
    public sealed class FrameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Frame frame) return frame.Value;
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d) return new Frame((int)d);
            return value;
        }
    }
}