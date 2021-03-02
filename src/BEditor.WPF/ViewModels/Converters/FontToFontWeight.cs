using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

using BEditor.Drawing;

namespace BEditor.ViewModels.Converters
{
    public class FontToFontWeight : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Font font)
            {
                return font.Weight switch
                {
                    FontStyleWeight.Invisible => FontWeights.Normal,
                    FontStyleWeight.Thin => FontWeights.Thin,
                    FontStyleWeight.ExtraLight => FontWeights.ExtraLight,
                    FontStyleWeight.Light => FontWeights.Light,
                    FontStyleWeight.Normal => FontWeights.Normal,
                    FontStyleWeight.Medium => FontWeights.Medium,
                    FontStyleWeight.SemiBold => FontWeights.SemiBold,
                    FontStyleWeight.Bold => FontWeights.Bold,
                    FontStyleWeight.ExtraBold => FontWeights.ExtraBold,
                    FontStyleWeight.Black => FontWeights.Black,
                    FontStyleWeight.ExtraBlack => FontWeights.ExtraBlack,
                    _ => FontWeights.Normal,
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
