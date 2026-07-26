using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;

using SkiaSharp;

namespace Beutl.HeadlessUITests;

/// <summary>
/// The preview frame cache stores 8-bit BGRA, so it is the last place a linear RgbaF16 frame is
/// quantized before the user sees it. Without dithering that quantization is what makes gradients band.
/// </summary>
[TestFixture]
public class FrameCacheDitherTests
{
    private const int Width = 1024;
    private const int Height = 8;

    /// <summary>A ramp narrow enough in value that 8-bit quantization produces wide flat bands.</summary>
    private static Bitmap CreateLinearRamp()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(Width, 0),
                   [new SKColor(30, 40, 70, 255), new SKColor(52, 66, 104, 255)],
                   [0f, 1f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = false })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
        }

        return new Bitmap(skBitmap);
    }

    private static int WidestFlatBand(Bitmap bitmap)
    {
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int bpp = bitmap.BytesPerPixel;
        int widest = 0;
        int runStart = 0;
        for (int x = 1; x < bitmap.Width; x++)
        {
            if (pixels[x * bpp] != pixels[runStart * bpp])
            {
                widest = Math.Max(widest, x - runStart);
                runStart = x;
            }
        }

        return Math.Max(widest, bitmap.Width - runStart);
    }

    [Test]
    public void RoundTrip_LinearF16Frame_KeepsBandsNarrow()
    {
        using var manager = new FrameCacheManager(
            new PixelSize(Width, Height),
            new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA))
        {
            IsEnabled = true
        };

        using (Ref<Bitmap> source = Ref<Bitmap>.Create(CreateLinearRamp()))
        {
            manager.Add(0, source);
        }

        Assert.That(manager.TryGet(0, out Ref<Bitmap>? cached), Is.True);
        using (cached)
        {
            // Undithered this ramp quantizes into bands tens of pixels wide; the dither in the cache
            // conversion breaks them into runs of a few pixels.
            Assert.That(WidestFlatBand(cached!.Value), Is.LessThan(16));
        }
    }
}
