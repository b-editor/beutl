using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;

using BEditor.Drawing;

namespace BEditor.ViewModels.Converters
{
    public class FontToFontFamily : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Font font)
            {
                var uri = new Uri(Path.GetDirectoryName(font.Filename)!);
                var family = new FontFamily(uri, font.FamilyName);

                return family;
            }

            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}