using System;
using System.Globalization;
using System.IO;

using Avalonia.Data.Converters;

using BEditor.Core.Service;

namespace BEditor.ViewModels.Converters
{
    public class ToFileNameConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path)
            {
                return Path.GetFileName(path);
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
