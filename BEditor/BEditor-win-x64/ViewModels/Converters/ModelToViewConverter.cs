using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

using BEditor.Views;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.ViewModels.Converters {
    public class SceneToTimeLine : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is Scene scene) return scene.GetCreateTimeLineView();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class SceneToPropertyTab : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is Scene scene) return scene.GetCreatePropertyTab();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class EffectElementToKeyFrame : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is EffectElement effect) return effect.GetCreateKeyFrameView();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class EffectElementToProperty : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is EffectElement effect) return effect.GetCreatePropertyView();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class ClipDataToProperty : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is BEditor.Core.Data.ObjectData.ClipData data) return data.GetCreatePropertyView();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class EasingFuncToProperty : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is EasingFunc easing) return easing.GetCreatePropertyView();
            throw new Exception();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
