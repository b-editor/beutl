using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using BEditor.Drawing;

namespace BEditor.Converters
{
    public sealed class FontToFontWeight : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Font font)
            {
                return font.Weight switch
                {
                    FontStyleWeight.Invisible => FontWeight.Normal,
                    FontStyleWeight.Thin => FontWeight.Thin,
                    FontStyleWeight.ExtraLight => FontWeight.ExtraLight,
                    FontStyleWeight.Light => FontWeight.Light,
                    FontStyleWeight.Normal => FontWeight.Normal,
                    FontStyleWeight.Medium => FontWeight.Medium,
                    FontStyleWeight.SemiBold => FontWeight.SemiBold,
                    FontStyleWeight.Bold => FontWeight.Bold,
                    FontStyleWeight.ExtraBold => FontWeight.ExtraBold,
                    FontStyleWeight.Black => FontWeight.Black,
                    FontStyleWeight.ExtraBlack => FontWeight.ExtraBlack,
                    _ => FontWeight.Normal,
                };
            }

            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}