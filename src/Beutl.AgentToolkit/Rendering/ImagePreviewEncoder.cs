using SkiaSharp;

namespace Beutl.AgentToolkit.Rendering;

internal static class ImagePreviewEncoder
{
    public const int DefaultMaxLongEdge = 768;

    public static byte[] EncodePngFile(string path, int maxLongEdge = DefaultMaxLongEdge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using SKBitmap bitmap = Decode(path);
        return EncodeBitmapToPng(bitmap, maxLongEdge);
    }

    public static byte[] EncodeBitmapToPng(SKBitmap bitmap, int maxLongEdge = DefaultMaxLongEdge)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            throw new ArgumentException("Bitmap dimensions must be positive.", nameof(bitmap));
        }

        double scale = ResolveScale(bitmap.Width, bitmap.Height, maxLongEdge);
        if (scale >= 1)
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            return EncodeImageToPng(image);
        }

        int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height));
        return EncodeSurfaceToPng(surface);
    }

    public static byte[] EncodeSurfaceToPng(SKSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        using SKImage image = surface.Snapshot();
        return EncodeImageToPng(image);
    }

    private static SKBitmap Decode(string path)
    {
        SKBitmap? bitmap = SKBitmap.Decode(path);
        if (bitmap is null)
        {
            throw new IOException($"Failed to read PNG image '{path}'.");
        }

        return bitmap;
    }

    private static byte[] EncodeImageToPng(SKImage image)
    {
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new IOException("Failed to encode PNG image.");
        return data.ToArray();
    }

    private static double ResolveScale(int width, int height, int maxLongEdge)
    {
        if (maxLongEdge <= 0)
        {
            return 1;
        }

        int longEdge = Math.Max(width, height);
        return longEdge <= maxLongEdge ? 1 : maxLongEdge / (double)longEdge;
    }
}
