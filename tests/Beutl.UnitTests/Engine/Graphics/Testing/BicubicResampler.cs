using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Testing;

/// <summary>
/// Bicubic resampler used to upscale a proxy-resolution bitmap to the reference resolution
/// before SSIM comparison. Delegates to Skia's cubic Mitchell sampling — the same filter
/// the compositor's <c>SKCanvas.Scale</c> uses when blitting a non-Identity-scale operation,
/// so the comparison stays apples-to-apples.
/// </summary>
internal static class BicubicResampler
{
    public static Bitmap Upscale(Bitmap source, int targetWidth, int targetHeight)
    {
        if (source.Width == targetWidth && source.Height == targetHeight)
            return source.Clone() as Bitmap ?? throw new InvalidOperationException("Clone returned non-Bitmap.");

        using var srcImage = SKImage.FromBitmap(source.SKBitmap);
        var info = new SKImageInfo(targetWidth, targetHeight, source.SKBitmap.ColorType, source.SKBitmap.AlphaType, source.SKBitmap.ColorSpace);
        var dst = new SKBitmap(info);
        srcImage.ScalePixels(dst.PeekPixels(), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return new Bitmap(dst);
    }
}
