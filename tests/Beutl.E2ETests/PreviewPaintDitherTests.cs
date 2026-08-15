using Beutl.Controls;

using SkiaSharp;

namespace Beutl.E2ETests;

/// <summary>
/// The preview draws linear RgbaF16 frames straight onto the 8-bit screen surface without going
/// through <see cref="Beutl.Media.Bitmap.Convert"/>, so the dither that keeps gradients from banding
/// has to come from the paint BitmapView builds.
/// </summary>
[TestFixture]
public class PreviewPaintDitherTests
{
    private const int Width = 1200;
    private const int Height = 64;

    /// <summary>A gradient narrow enough in value that 8-bit quantization produces wide flat bands.</summary>
    private static SKBitmap CreateLinearGradient()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(Width, 0),
                   [new SKColor(30, 40, 70, 255), new SKColor(52, 66, 104, 255)],
                   [0f, 1f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = false })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
        }

        return bitmap;
    }

    /// <summary>Draws onto an 8-bit sRGB surface the way the preview's draw operation does.</summary>
    private static SKBitmap DrawToScreenSurface(SKBitmap source, SKPaint paint)
    {
        var screen = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());
        var surface = new SKBitmap(screen);
        using (var canvas = new SKCanvas(surface))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawImage(image, new SKRect(0, 0, Width, Height), new SKRect(0, 0, Width, Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        return surface;
    }

    /// <summary>Longest run of pixels sharing one quantized value along row 0 — the visible band width.</summary>
    private static int WidestFlatBand(SKBitmap bitmap)
    {
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int widest = 0;
        int runStart = 0;
        for (int x = 1; x < bitmap.Width; x++)
        {
            if (pixels[x * 4] != pixels[runStart * 4])
            {
                widest = Math.Max(widest, x - runStart);
                runStart = x;
            }
        }

        return Math.Max(widest, bitmap.Width - runStart);
    }

    [Test]
    public void CreatePreviewPaint_EnablesDither()
    {
        using SKPaint plain = BitmapView.CreatePreviewPaint(null);
        Assert.That(plain.IsDither, Is.True);

        using var filter = SKColorFilter.CreateLinearToSrgbGamma();
        using SKPaint filtered = BitmapView.CreatePreviewPaint(filter);
        Assert.That(filtered.IsDither, Is.True);
        Assert.That(filtered.ColorFilter, Is.SameAs(filter));
    }

    [Test]
    public void PreviewPaint_NarrowsBandsVersusAnUnditheredDraw()
    {
        using SKBitmap gradient = CreateLinearGradient();

        using var filter = SKColorFilter.CreateLinearToSrgbGamma();
        using SKPaint dithered = BitmapView.CreatePreviewPaint(filter);
        using var undithered = new SKPaint { ColorFilter = filter, IsDither = false };

        using SKBitmap withDither = DrawToScreenSurface(gradient, dithered);
        using SKBitmap withoutDither = DrawToScreenSurface(gradient, undithered);

        Assert.That(WidestFlatBand(withDither), Is.LessThan(WidestFlatBand(withoutDither)));
    }
}
