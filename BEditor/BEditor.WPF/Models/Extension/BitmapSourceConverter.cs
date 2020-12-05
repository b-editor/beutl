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

            dst.WritePixels(new Int32Rect(0, 0, w, h), src.Data, src.Stride, 0);
        }

        public unsafe static void ToImage(this BitmapSource src, Image<BGRA32> dst)
        {
            fixed (BGRA32* data = dst.Data)
            {
                src.CopyPixels(Int32Rect.Empty, (IntPtr)data, dst.Height * dst.Stride, dst.Stride);
            }
        }

        public static Bitmap ToBitmap(this BitmapSource src)
        {
            var bitmap = new Bitmap(src.PixelWidth, src.PixelHeight, PixelFormat.Format32bppPArgb);
            var bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            src.CopyPixels(Int32Rect.Empty, bitmapData.Scan0, bitmapData.Height * bitmapData.Stride, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);

            return bitmap;
        }
    }
}
