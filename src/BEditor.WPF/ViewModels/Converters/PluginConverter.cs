using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Data;

using BEditor.Plugin;

namespace BEditor.ViewModels.Converters
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
    public sealed class PluginAuthorConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PluginObject plugin)
            {
                var assembly = plugin.GetType().Assembly;

                var attributes = assembly.CustomAttributes.ToArray();

                var a = Array.Find(attributes, a => a.AttributeType == typeof(AssemblyCompanyAttribute));

                return a?.ConstructorArguments.FirstOrDefault().Value;
            }

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}