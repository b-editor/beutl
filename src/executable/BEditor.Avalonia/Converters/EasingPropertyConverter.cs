using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Data.Property.Easing;
using BEditor.Extensions;

namespace BEditor.Converters
{
    public sealed class EasingPropertyConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EasingFunc f) return f.GetCreateEasingFuncView();

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}