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
            static int Digit(int num)
            {
                // Mathf.Log10(0)はNegativeInfinityを返すため、別途処理する。
                return (num == 0) ? 1 : ((int)MathF.Log10(num) + 1);
            }

            if (value is Frame frame1 && AppModel.Current.Project != null)
            {
                var scene = AppModel.Current.Project.CurrentScene;
                var rate = scene.Parent.Framerate;
                var time = frame1.ToTimeSpan(rate);
                var str = time.ToString(@"hh\:mm\:ss");

                var frame2 = Frame.FromTimeSpan(TimeSpan.Parse(str), rate);
                var f = frame1 - frame2;
                var format = $"D{Digit(rate)}";

                return str + $" '{f.Value.ToString(format)}";
            }

            return null;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}