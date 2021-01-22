using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Core.Data.Property.Easing;
using BEditor.Views;

namespace BEditor.ViewModels.Converters
{
    public class EasingFuncToProperty : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EasingFunc easing) return easing.GetCreatePropertyView();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
