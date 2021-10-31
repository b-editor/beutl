using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Models.ManagePlugins;
using BEditor.Plugin;

namespace BEditor.Converters
{
    // 'PluginObject'から'{プラグイン名} {バージョン}'にフォーマット
    // e.g.:'SamplePlugin 0.0.0'
    public sealed class PluginNameConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PluginObject plugin)
            {
                var assembly = plugin is DummyPlugin dummy ? dummy.Assembly : plugin.GetType().Assembly;
                var name = assembly.GetName();
                return $"{name.Name} {name.Version}";
            }

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return default;
        }
    }
}