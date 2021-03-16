using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media.Imaging;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Models.Extension
{
    public static class BitmapSourceConverter
    {
        public static BitmapSource ToBitmapSource(this Image<BGRA32> src)
        {
            var Bitmap = new WriteableBitmap(src.Width, src.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            ToWriteableBitmap(src, Bitmap);
            return Bitmap;
        }

        public static unsafe void ToWriteableBitmap(Image<BGRA32> src, WriteableBitmap dst)
        {
            if (src == null)
            {
                throw new ArgumentNullException(nameof(src));
            }

            if (dst == null)
            {
                throw new ArgumentNullException(nameof(dst));
            }

            if (src.Width != dst.PixelWidth || src.Height != dst.PixelHeight)
            {
                throw new ArgumentException("size of src must be equal to size of dst");
            }

            int w = src.Width;
            int h = src.Height;

            fixed(BGRA32* data = src.Data)
            {
                dst.WritePixels(new Int32Rect(0, 0, w, h), (IntPtr)data, src.DataSize, src.Stride);
            }
        }
    }
}
