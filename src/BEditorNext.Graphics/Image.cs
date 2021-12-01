using BEditorNext.Graphics.Effects;
using BEditorNext.Graphics.Operations;
using BEditorNext.Graphics.Pixel;

using OpenCvSharp;

using SkiaSharp;

namespace BEditorNext.Graphics;

public static unsafe partial class Image
{
    private static readonly Dictionary<string, EncodedImageFormat> _extensionToFormat = new()
    {
        { ".bmp", EncodedImageFormat.Bmp },
        { ".gif", EncodedImageFormat.Gif },
        { ".ico", EncodedImageFormat.Ico },
        { ".jpg", EncodedImageFormat.Jpeg },
        { ".jpeg", EncodedImageFormat.Jpeg },
        { ".png", EncodedImageFormat.Png },
        { ".wbmp", EncodedImageFormat.Wbmp },
        { ".webp", EncodedImageFormat.Webp },
        { ".pkm", EncodedImageFormat.Pkm },
        { ".ktx", EncodedImageFormat.Ktx },
        { ".astc", EncodedImageFormat.Astc },
        { ".dng", EncodedImageFormat.Dng },
        { ".heif", EncodedImageFormat.Heif },
    };

    public static Bitmap<Bgra8888> ApplyEffect(this Bitmap<Bgra8888> image, IEffect effect)
    {
        var applyed = effect.Apply(image);

        if (applyed == image)
        {
            return (Bitmap<Bgra8888>)applyed.Clone();
        }
        else
        {
            return applyed;
        }
    }

    public static void AlphaSubtract(this Bitmap<Bgra8888> image, Bitmap<Bgra8888> mask)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        if (mask is null) throw new ArgumentNullException(nameof(mask));
        image.ThrowIfDisposed();
        mask.ThrowIfDisposed();

        var data = (Bgra8888*)image.Data;
        var maskptr = (Bgra8888*)mask.Data;
        Parallel.For(0, image.Width * image.Height, new AlphaSubtractOperation(data, maskptr).Invoke);
    }

    public static Bitmap<Grayscale8> AlphaMap(this Bitmap<Bgra8888> image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        image.ThrowIfDisposed();

        var result = new Bitmap<Grayscale8>(image.Width, image.Height);
        var src = (Bgra8888*)image.Data;
        var dst = (Grayscale8*)result.Data;

        Parallel.For(0, image.Width * image.Height, new AlphaMapOperation(src, dst).Invoke);

        return result;
    }

    public static Bitmap<Bgra8888> ToBitmap(this Mat self)
    {
        using var bmp = self.ToSKBitmap();
        return bmp.ToBitmap();
    }

    public static Bitmap<Bgra8888> ToBitmap(this SKBitmap self)
    {
        if (self.ColorType is SKColorType.Bgra8888)
        {
            var result = new Bitmap<Bgra8888>(self.Width, self.Height);

            Buffer.MemoryCopy((void*)self.GetPixels(), (void*)result.Data, result.ByteCount, result.ByteCount);

            return result;
        }
        else
        {
            using var bmp = new SKBitmap(new SKImageInfo(self.Width, self.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            canvas.DrawBitmap(self, SKPoint.Empty);

            var result = new Bitmap<Bgra8888>(self.Width, self.Height);

            Buffer.MemoryCopy((void*)self.GetPixels(), (void*)result.Data, result.ByteCount, result.ByteCount);

            return result;
        }
    }

    public static SKBitmap ToSKBitmap(this Bitmap<Bgra8888> self)
    {
        var result = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));

        result.SetPixels(self.Data);

        return result;
    }

    public static SKBitmap ToSKBitmap(this Mat self)
    {
        var result = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));

        result.SetPixels(self.Data);

        return result;
    }

    public static Mat ToMat(this Bitmap<Bgra8888> self)
    {
        return new Mat(self.Height, self.Width, MatType.CV_8UC4, self.Data);
    }

    public static Mat ToMat(this SKBitmap self)
    {
        using var bmp = self.ToBitmap();
        return bmp.ToMat();
    }

    internal static EncodedImageFormat ToImageFormat(string filename)
    {
        var ex = Path.GetExtension(filename);

        if (string.IsNullOrEmpty(filename)) throw new IOException();

        return _extensionToFormat.TryGetValue(ex, out var value) ? value : EncodedImageFormat.Png;
    }
}
