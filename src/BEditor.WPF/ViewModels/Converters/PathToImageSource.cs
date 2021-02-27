using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BEditor.Models.Extension;

namespace BEditor.ViewModels.Converters
{
    public sealed class PathToImageSource : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is string path)
            {
                using var img = Drawing.Image.Decode(path);
                
                return img.ToBitmapSource();
            }

            return new WriteableBitmap(1, 1, 48, 48, PixelFormats.Bgra32, BitmapPalettes.WebPaletteTransparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
