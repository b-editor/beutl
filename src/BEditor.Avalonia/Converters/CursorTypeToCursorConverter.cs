using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Input;

namespace BEditor.Converters
{
    public sealed class CursorTypeToCursorConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StandardCursorType f) return new Cursor(f);

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}