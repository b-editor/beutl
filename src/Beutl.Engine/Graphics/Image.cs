using System.Collections.Frozen;
using System.Collections.Immutable;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

public static partial class Image
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

    public static Bitmap ToBitmap(
        this SKImage self,
        BitmapColorType? colorType = null,
        BitmapAlphaType? alphaType = null,
        BitmapColorSpace? colorSpace = null)
    {
        var colorType2 = colorType ?? BitmapColorTypeExtensions.FromSKColorType(self.ColorType);
        var alphaType2 = alphaType ?? BitmapAlphaTypeExtensions.FromSKAlphaType(self.AlphaType);
        var colorSpace2 = colorSpace ?? BitmapColorSpace.FromSKColorSpace(self.ColorSpace);
        var bitmap = new Bitmap(self.Width, self.Height, colorType2, alphaType2, colorSpace2);
        self.ReadPixels(
            bitmap.SKBitmap.Info,
            bitmap.Data,
            bitmap.RowBytes);

        return bitmap;
    }

    internal static EncodedImageFormat ToImageFormat(string filename)
    {
        string? ex = Path.GetExtension(filename);

        if (string.IsNullOrEmpty(filename))
            throw new IOException();

        return s_extensionToFormat.TryGetValue(ex, out EncodedImageFormat value) ? value : EncodedImageFormat.Png;
    }
}
