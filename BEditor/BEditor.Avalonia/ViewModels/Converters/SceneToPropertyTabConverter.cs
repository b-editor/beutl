using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Core.Data;
using BEditor.Core.Service;
using BEditor.Views;

namespace BEditor.ViewModels.Converters
{
    public class SceneToPropertyTabConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Scene scene)
            {
                return scene.GetCreatePropertyTab();
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
