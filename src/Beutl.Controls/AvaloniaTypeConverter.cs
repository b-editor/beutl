using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Media;
using PixelSize = Avalonia.PixelSize;
using Point = Avalonia.Point;

namespace Beutl.Controls;

public static class AvaloniaTypeConverter
{
    public static Avalonia.Media.Color ToAvaColor(this in Media.Color color)
    {
        return Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Media.Color ToBtlColor(this FluentAvalonia.UI.Media.Color2 c)
    {
        return new Media.Color(c.A, c.R, c.G, c.B);
    }

    public static Media.Color ToBtlColor(this in Avalonia.Media.Color color)
    {
        return Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Matrix ToAvaMatrix(this in Graphics.Matrix matrix)
    {
        return new Matrix(
            matrix.M11, matrix.M12, matrix.M13,
            matrix.M21, matrix.M22, matrix.M23,
            matrix.M31, matrix.M32, matrix.M33);
    }

    public static Graphics.Matrix ToBtlMatrix(this in Matrix matrix)
    {
        return new Graphics.Matrix(
            (float)matrix.M11, (float)matrix.M12, (float)matrix.M13,
            (float)matrix.M21, (float)matrix.M22, (float)matrix.M23,
            (float)matrix.M31, (float)matrix.M32, (float)matrix.M33);
    }

    public static Point ToAvaPoint(this in Graphics.Point point)
    {
        return new Point(point.X, point.Y);
    }

    public static Graphics.Point ToBtlPoint(this in Point point)
    {
        return new Graphics.Point((float)point.X, (float)point.Y);
    }

    public static PixelFormat? ToAvaPixelFormat(this BitmapColorType colorType)
    {
        return colorType switch
        {
            BitmapColorType.Bgra8888 => PixelFormats.Bgra8888,
            BitmapColorType.Rgba8888 => PixelFormats.Rgba8888,
            BitmapColorType.Rgb565 => PixelFormats.Rgb565,
            BitmapColorType.Gray8 => PixelFormats.Gray8,
            _ => null
        };
    }

    public static AlphaFormat? ToAvaAlphaFormat(this BitmapAlphaType alphaType)
    {
        return alphaType switch
        {
            BitmapAlphaType.Premul => AlphaFormat.Premul,
            BitmapAlphaType.Unpremul => AlphaFormat.Unpremul,
            _ => null
        };
    }

    private static unsafe void CopyBitmapToFramebuffer(Media.Bitmap bitmap, ILockedFramebuffer locked)
    {
        int srcRowBytes = bitmap.RowBytes;
        int dstRowBytes = locked.RowBytes;
        int copyBytes = bitmap.Width * bitmap.BytesPerPixel;

        if (srcRowBytes == dstRowBytes)
        {
            Buffer.MemoryCopy((void*)bitmap.Data, (void*)locked.Address, bitmap.ByteCount, bitmap.ByteCount);
        }
        else
        {
            byte* src = (byte*)bitmap.Data;
            byte* dst = (byte*)locked.Address;
            for (int y = 0; y < bitmap.Height; y++)
            {
                Buffer.MemoryCopy(src + (long)y * srcRowBytes, dst + (long)y * dstRowBytes, copyBytes, copyBytes);
            }
        }
    }

    public static unsafe WriteableBitmap ToAvaWriteableBitmap(this Media.Bitmap bitmap, WriteableBitmap? previous = null)
    {
        var pixelFormat = bitmap.ColorType.ToAvaPixelFormat();
        var alphaFormat = bitmap.AlphaType.ToAvaAlphaFormat();
        if (pixelFormat == null || alphaFormat == null)
        {
            using var converted = bitmap.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            return converted.ToAvaWriteableBitmap(previous);
        }

        if (previous != null &&
            previous.PixelSize.Width == bitmap.Width &&
            previous.PixelSize.Height == bitmap.Height &&
            previous.Format == pixelFormat &&
            previous.AlphaFormat == alphaFormat)
        {
            using var locked = previous.Lock();
            CopyBitmapToFramebuffer(bitmap, locked);
            return previous;
        }
        else
        {
            var pixelSize = new PixelSize(bitmap.Width, bitmap.Height);
            var writeableBitmap = new WriteableBitmap(pixelSize, new Vector(96, 96), pixelFormat, alphaFormat);

            using (var locked = writeableBitmap.Lock())
            {
                CopyBitmapToFramebuffer(bitmap, locked);
            }

            previous?.Dispose();
            return writeableBitmap;
        }
    }
}
