using System.Collections.Frozen;
using System.Collections.Immutable;

using Beutl.Graphics.Operations;
using Beutl.Media;
using Beutl.Media.Pixel;

using OpenCvSharp;

using SkiaSharp;

namespace Beutl.Graphics;

public static unsafe partial class Image
{
    private static readonly FrozenDictionary<string, EncodedImageFormat> s_extensionToFormat;

    static Image()
    {
        ImmutableArray<KeyValuePair<string, EncodedImageFormat>> list =
        [
            new KeyValuePair<string, EncodedImageFormat>(".bmp", EncodedImageFormat.Bmp),
            new KeyValuePair<string, EncodedImageFormat>(".gif", EncodedImageFormat.Gif),
            new KeyValuePair<string, EncodedImageFormat>(".ico", EncodedImageFormat.Ico),
            new KeyValuePair<string, EncodedImageFormat>(".jpg", EncodedImageFormat.Jpeg),
            new KeyValuePair<string, EncodedImageFormat>(".jpeg", EncodedImageFormat.Jpeg),
            new KeyValuePair<string, EncodedImageFormat>(".png", EncodedImageFormat.Png),
            new KeyValuePair<string, EncodedImageFormat>(".wbmp", EncodedImageFormat.Wbmp),
            new KeyValuePair<string, EncodedImageFormat>(".webp", EncodedImageFormat.Webp),
            new KeyValuePair<string, EncodedImageFormat>(".pkm", EncodedImageFormat.Pkm),
            new KeyValuePair<string, EncodedImageFormat>(".ktx", EncodedImageFormat.Ktx),
            new KeyValuePair<string, EncodedImageFormat>(".astc", EncodedImageFormat.Astc),
            new KeyValuePair<string, EncodedImageFormat>(".dng", EncodedImageFormat.Dng),
            new KeyValuePair<string, EncodedImageFormat>(".heif", EncodedImageFormat.Heif),
        ];

        s_extensionToFormat = list.ToFrozenDictionary();
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

            Buffer.MemoryCopy((void*)bmp.GetPixels(), (void*)result.Data, result.ByteCount, result.ByteCount);

            return result;
        }
    }

    public static Bitmap<Bgra8888> ToBitmap(this SKImage self)
    {
        var result = new Bitmap<Bgra8888>(self.Width, self.Height);
        self.ReadPixels(new SKImageInfo(self.Width, self.Height, SKColorType.Bgra8888), result.Data);
        return result;
    }

    public static SKBitmap ToSKBitmap(this Bitmap<Bgra8888> self)
    {
        var result = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));

        result.SetPixels(self.Data);

        return result;
    }

    public static SKBitmap ToSKBitmap(this IBitmap self)
    {
        if (self is Bitmap<Bgra8888> bgra8888)
        {
            var result = new SKBitmap(new(bgra8888.Width, bgra8888.Height, SKColorType.Bgra8888));

            result.SetPixels(bgra8888.Data);

            return result;
        }
        else if (self is Bitmap<Bgra4444> bgra4444)
        {
            var result = new SKBitmap(new(bgra4444.Width, bgra4444.Height, SKColorType.Argb4444));

            result.SetPixels(bgra4444.Data);

            return result;
        }
        else if (self is Bitmap<Grayscale8> grayscale8)
        {
            var result = new SKBitmap(new(grayscale8.Width, grayscale8.Height, SKColorType.Alpha8));

            result.SetPixels(grayscale8.Data);

            return result;
        }
        else
        {
            using Bitmap<Bgra8888> typed = self.Convert<Bgra8888>();
            var result = new SKBitmap(new(typed.Width, typed.Height, SKColorType.Bgra8888));

            result.SetPixels(typed.Data);

            return result;
        }
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

    internal static EncodedImageFormat ToImageFormat(string filename)
    {
        string? ex = Path.GetExtension(filename);

        if (string.IsNullOrEmpty(filename))
            throw new IOException();

        return s_extensionToFormat.TryGetValue(ex, out EncodedImageFormat value) ? value : EncodedImageFormat.Png;
    }
}
