using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Extensions;

namespace BEditor.Converters
{
    public sealed class GetNameConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Project proj) return proj.Name;
            else if (value is Scene scene) return scene.SceneName;
            else if (value is ClipElement clip) return clip.Name;
            else if (value is EffectElement effect) return effect.Name;
            else if (value is IPropertyElement property) return property.PropertyMetadata?.Name ?? string.Empty;
            else if (value is EasingFunc ease) return ease.GetType().Name;
            else return value.GetType().Name;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}