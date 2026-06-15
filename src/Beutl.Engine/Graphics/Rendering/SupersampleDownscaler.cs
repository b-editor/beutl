using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Resamples a supersampled / device-resolution frame down to the logical output resolution
/// (feature 003, FR-026/FR-034). Single source of truth for the sampler choice so the export
/// pipeline (<c>FrameProviderImpl</c>), the editor still-frame path (<c>PlayerViewModel.DrawFrame</c>),
/// and the golden tests all resample identically.
/// </summary>
public static class SupersampleDownscaler
{
    /// <summary>
    /// Returns a bitmap that is exactly <paramref name="target"/> pixels. When <paramref name="source"/>
    /// is already that size it is returned unchanged (byte-identical); otherwise a new bitmap is returned and
    /// the caller must dispose <paramref name="source"/>. Use <see cref="object.ReferenceEquals"/> to tell the two cases apart.
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
    /// The resample kernel for a given supersample factor (feature 003, SC-009): Mitchell cubic for
    /// <c>≤ 2×</c>, trilinear + mipmaps for <c>&gt; 2×</c> (e.g. 4×) to avoid undersampling artifacts.
    /// </summary>
    public static SKSamplingOptions SamplingFor(float renderScale)
        => renderScale > 2f
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
}
