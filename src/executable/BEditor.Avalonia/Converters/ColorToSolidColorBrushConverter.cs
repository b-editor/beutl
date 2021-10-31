using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using BEditor.Extensions;

namespace BEditor.Converters
{
    // 'BEditor.Drawing'または'Avalonia.Media'の色を'SolidColorBrush'に変換
    public sealed class ColorToSolidColorBrushConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Drawing.Color f) return new SolidColorBrush(f.ToAvalonia());
            else if (value is Color c) return new SolidColorBrush(c);

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}