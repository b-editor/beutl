using System;
using System.Globalization;

using Avalonia.Data.Converters;

using BEditor.Media;
using BEditor.Models;

namespace BEditor.Converters
{
    public sealed class FrameToTimeConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Frame frame1)
            {
                var scene = AppModel.Current.Project.CurrentScene;
                var rate = scene.Parent.Framerate;
                var time = frame1.ToTimeSpan(rate);
                var str = time.ToString(@"hh\:mm\:ss");

                var frame2 = Frame.FromTimeSpan(TimeSpan.Parse(str), rate);

                var f = frame1 - frame2;

                return str + $" `{f.Value}";
            }

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}