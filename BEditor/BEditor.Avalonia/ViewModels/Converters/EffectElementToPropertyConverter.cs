using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Core.Data;
using BEditor.Views;

namespace BEditor.ViewModels.Converters
{
    public class EffectElementToPropertyConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EffectElement effect)
            {
                return effect.GetCreatePropertyView();
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
