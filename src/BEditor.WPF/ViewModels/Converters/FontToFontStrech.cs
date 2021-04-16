using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

using BEditor.Drawing;

namespace BEditor.ViewModels.Converters
{
    public class FontToFontStrech : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Font font)
            {
                return font.Width switch
                {
                    FontStyleWidth.UltraCondensed => FontStretches.UltraCondensed,
                    FontStyleWidth.ExtraCondensed => FontStretches.ExtraCondensed,
                    FontStyleWidth.Condensed => FontStretches.Condensed,
                    FontStyleWidth.SemiCondensed => FontStretches.SemiCondensed,
                    FontStyleWidth.Normal => FontStretches.Normal,
                    FontStyleWidth.SemiExpanded => FontStretches.SemiExpanded,
                    FontStyleWidth.Expanded => FontStretches.Expanded,
                    FontStyleWidth.ExtraExpanded => FontStretches.ExtraExpanded,
                    FontStyleWidth.UltraExpanded => FontStretches.UltraExpanded,
                    _ => FontStretches.Normal,
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