using System;
using System.Diagnostics;

using Avalonia.Media.Imaging;
using Avalonia.Platform;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions
{
    public static class BitmapConverter
    {
        public static WriteableBitmap ToBitmapSource(this Image<BGRA32> src)
        {
            var Bitmap = new WriteableBitmap(new(src.Width, src.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            ToWriteableBitmap(src, Bitmap);
            return Bitmap;
        }

        public static unsafe void ToWriteableBitmap(Image<BGRA32> src, WriteableBitmap dst)
        {
            if (src is null) throw new ArgumentNullException(nameof(src));
            if (dst is null) throw new ArgumentNullException(nameof(dst));

            var w = src.Width;
            var h = src.Height;

            if (src.Width != dst.PixelSize.Width || src.Height != dst.PixelSize.Height)
            {
                throw new ArgumentException("size of src must be equal to size of dst");
            }
            using var locked = dst.Lock();

            fixed (BGRA32* data = src.Data)
            {
                Buffer.MemoryCopy(data, (void*)locked.Address, src.DataSize, src.DataSize);
            }
        }

        public static unsafe Image<BGRA32> ToImage(this WriteableBitmap bitmap)
        {
            Image<BGRA32>? result;
            using var locked = bitmap.Lock();

            if (locked.Format is PixelFormat.Bgra8888)
            {
                result = new(locked.Size.Width, locked.Size.Height);
                fixed (BGRA32* dst = result.Data)
                {
                    Buffer.MemoryCopy((void*)locked.Address, dst, result.DataSize, result.DataSize);
                }
            }
            else
            {
                result = new(locked.Size.Width, locked.Size.Height);
                Debug.Fail(string.Empty);
            }

            return result;
        }
    }
}