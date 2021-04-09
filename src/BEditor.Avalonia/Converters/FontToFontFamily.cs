using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Data.Converters;
using Avalonia.Media;

using BEditor.Drawing;

namespace BEditor.Converters
{
    public class FontToFontFamily : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Font font)
            {
                var uri = new Uri(Path.GetDirectoryName(font.Filename)!);
                return new FontFamily(uri, font.FamilyName);
            }

            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null!;
        }
    }
}
