using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
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

            fixed (BGRA32* data = src.Data)
            {
                dst.WritePixels(new Int32Rect(0, 0, w, h), (IntPtr)data, src.DataSize, src.Stride);
            }
        }

        public static unsafe Image<BGRA32> ToImage(this BitmapSource bitmap)
        {
            Image<BGRA32>? result;

            if (bitmap.Format == PixelFormats.Bgra32)
            {
                result = new Image<BGRA32>(bitmap.PixelWidth, bitmap.PixelHeight);
                fixed (BGRA32* dst = result.Data)
                {
                    bitmap.CopyPixels(new Int32Rect(0, 0, result.Width, result.Height), new IntPtr(dst), result.DataSize, result.Stride);
                }
            }
            else if (bitmap.Format == PixelFormats.Bgr24)
            {
                using var bgrImg = new Image<BGR24>(bitmap.PixelWidth, bitmap.PixelHeight);

                fixed (BGR24* dst = bgrImg.Data)
                {
                    bitmap.CopyPixels(new Int32Rect(0, 0, bgrImg.Width, bgrImg.Height), new IntPtr(dst), bgrImg.DataSize, bgrImg.Stride);
                }

                result = bgrImg.Convert<BGRA32>();
            }
            else if(bitmap.Format == PixelFormats.Bgr32)
            {
                var bgrImg = new Image<BGRA32>(bitmap.PixelWidth, bitmap.PixelHeight);

                fixed (BGRA32* dst = bgrImg.Data)
                {
                    bitmap.CopyPixels(new Int32Rect(0, 0, bgrImg.Width, bgrImg.Height), new IntPtr(dst), bgrImg.DataSize, bgrImg.Stride);
                }

                result = bgrImg;
            }
            else
            {
                result = new(bitmap.PixelWidth, bitmap.PixelHeight);
                Debug.Assert(false);
            }

            return result;
        }
    }
}
