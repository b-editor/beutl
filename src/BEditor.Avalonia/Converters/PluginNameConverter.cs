using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Data.Converters;

using BEditor.Plugin;

namespace BEditor.Converters
{
    public sealed class PluginNameConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PluginObject plugin)
            {
                var assembly = plugin.GetType().Assembly;
                var name = assembly.GetName();
                return $"{name.Name} {name.Version}";
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}