using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Resamples a supersampled frame down to the logical output resolution.
/// </summary>
public static class SupersampleDownscaler
{
    /// <summary>
    /// Returns a bitmap at exactly <paramref name="target"/> pixels. When <paramref name="source"/>
    /// is already that size it is returned unchanged; otherwise a new bitmap is allocated.
    /// Use <see cref="object.ReferenceEquals"/> to tell the two cases apart.
    /// </summary>
    public static Bitmap ToFrameSize(Bitmap source, PixelSize target, float renderScale)
    {
        if (source.Width == target.Width && source.Height == target.Height)
        {
            return source;
        }

        var info = new SKImageInfo(target.Width, target.Height,
            source.SKBitmap.ColorType, source.SKBitmap.AlphaType, source.SKBitmap.ColorSpace);
        var dst = new SKBitmap(info);
        SKSamplingOptions sampling = SamplingFor(renderScale);

        using (var canvas = new SKCanvas(dst))
        using (SKImage img = SKImage.FromBitmap(source.SKBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(img, new SKRect(0, 0, target.Width, target.Height), sampling);
        }

        return new Bitmap(dst);
    }

    /// <summary>
    /// Resample kernel: Mitchell cubic for &lt;= 2x, trilinear + mipmaps for &gt; 2x.
    /// Above 2x, mipmaps prevent undersampling artifacts (Moire / ringing).
    /// </summary>
    public static SKSamplingOptions SamplingFor(float renderScale)
        => renderScale > 2f
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
}
