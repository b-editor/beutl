using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Avalonia.Data.Converters;

using BEditor.Plugin;

namespace BEditor.Converters
{
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
            return null;
        }
    }
}