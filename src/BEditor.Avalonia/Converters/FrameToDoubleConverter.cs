using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Media;

namespace BEditor.Converters
{
    public sealed class FrameToDoubleConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Frame f) return (double)f.Value;

            return 0.0;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double f) return new Frame((int)f);

            return Frame.Zero;
        }
    }
}