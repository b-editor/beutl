using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Data;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.Converters
{
    public sealed class BackgroundTypeConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Project f)
            {
                return f.GetObservable(ProjectConfig.BackgroundTypeProperty).ToReactiveProperty();
            }

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}