using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;

namespace BEditor.ViewModels.Converters
{
    public class PropertyHeaderTextConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            return $"{values[0]}\n{values[1]}\n\n{values[2]}";
        }
    }
}
