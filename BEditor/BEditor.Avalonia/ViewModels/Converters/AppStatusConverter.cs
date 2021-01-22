using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Avalonia.Data.Converters;

using BEditor.Core.Data;
using BEditor.Core.Properties;
using BEditor.Core.Service;

namespace BEditor.ViewModels.Converters
{
    public class AppStatusToPlayerIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Status status)
            {
                return status is Status.Playing;
            }
            else return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
