using System;
using System.Globalization;
using System.Windows.Data;

using BEditor.Data;
using BEditor.Data.Property.Easing;
using BEditor.Views;

namespace BEditor.ViewModels.Converters
{
    public sealed class SceneToTimeLine : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Scene scene) return scene.GetCreateTimeLineView();

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public sealed class SceneToPropertyTab : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Scene scene) return scene.GetCreatePropertyTab();

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public sealed class EffectElementToKeyFrame : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EffectElement effect) return effect.GetCreateKeyFrameView();

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public sealed class EffectElementToProperty : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EffectElement effect) return effect.GetCreatePropertyView();

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public sealed class ClipDataToProperty : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ClipElement data) return data.GetCreatePropertyView();

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public sealed class EasingFuncToProperty : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EasingFunc easing) return easing.GetCreatePropertyView();

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}