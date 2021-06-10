using System;
using System.Globalization;
using System.IO;

using Avalonia.Data.Converters;
using Avalonia.Media;

using BEditor.Drawing;

namespace BEditor.Converters
{
    public sealed class FontToFontFamily : IValueConverter
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