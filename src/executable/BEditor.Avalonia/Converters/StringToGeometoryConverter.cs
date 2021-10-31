using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BEditor.Converters
{
    // 文字列から'Geometry'に変換
    public sealed class StringToGeometoryConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string f) return Geometry.Parse(f);

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}