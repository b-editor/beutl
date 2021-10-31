using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace BEditor.Converters
{
    // 文字列から'Uri'に変換
    public sealed class StringToUriConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string f)
            {
                return new Uri(f);
            }

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}