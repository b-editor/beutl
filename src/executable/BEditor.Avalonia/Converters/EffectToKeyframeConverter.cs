using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Data;
using BEditor.Extensions;

namespace BEditor.Converters
{
    public sealed class EffectToKeyframeConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EffectElement f) return f.GetCreateKeyFrameViewSafe();

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}