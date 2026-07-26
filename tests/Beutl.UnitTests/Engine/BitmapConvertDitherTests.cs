using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine;

/// <summary>
/// Characterizes the dithering <see cref="Bitmap.Convert"/> applies when the destination has less
/// precision than the source, which is what keeps gradients from banding on the way out of the
/// linear RgbaF16 render target.
/// </summary>
[TestFixture]
public class BitmapConvertDitherTests
{
    private const int Width = 1024;
    private const int Height = 8;

    /// <summary>Builds a black-to-white ramp in the render target's format (linear RgbaF16).</summary>
    private static Bitmap CreateLinearRamp()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(Width, 0),
                   [new SKColor(0, 0, 0, 255), new SKColor(255, 255, 255, 255)],
                   [0f, 1f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = false })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
        }

        return new Bitmap(skBitmap);
    }

    /// <summary>Longest run of pixels sharing one quantized value along row 0 — the visible band width.</summary>
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

    /// <summary>Converts to 8-bit sRGB with dithering off, i.e. what <see cref="Bitmap.Convert"/> used to do.</summary>
    private static Bitmap ConvertWithoutDither(Bitmap source)
    {
        var destInfo = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888,
            SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        var destBitmap = new SKBitmap(destInfo);
        using (var canvas = new SKCanvas(destBitmap))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src, IsDither = false })
        {
            canvas.DrawBitmap(source.SKBitmap, SKPoint.Empty, paint);
        }

        return new Bitmap(destBitmap);
    }

    [Test]
    public void Convert_LinearF16ToSrgb8_NarrowsBandsVersusUndithered()
    {
        using Bitmap ramp = CreateLinearRamp();
        using Bitmap dithered = ramp.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        using Bitmap undithered = ConvertWithoutDither(ramp);

        Assert.That(WidestFlatBand(dithered), Is.LessThan(WidestFlatBand(undithered)));
    }

    [Test]
    public void Convert_LinearF16ToSrgb8_RowsDifferFromDitherPattern()
    {
        using Bitmap ramp = CreateLinearRamp();
        using Bitmap converted = ramp.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

        // The source is constant down each column, so any row-to-row difference is the dither.
        ReadOnlySpan<byte> row0 = converted.GetRow(0);
        int differingRows = 0;
        for (int y = 1; y < converted.Height; y++)
        {
            if (!converted.GetRow(y).SequenceEqual(row0))
            {
                differingRows++;
            }
        }

        Assert.That(differingRows, Is.GreaterThan(0));
    }

    [TestCase(0, 0, 0)]
    [TestCase(255, 255, 255)]
    [TestCase(128, 128, 128)]
    [TestCase(37, 99, 235)]
    public void Convert_SolidFill_StaysWithinOneLevelOfTheSourceColor(byte r, byte g, byte b)
    {
        var info = new SKImageInfo(64, 64, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        using (var paint = new SKPaint { Color = new SKColor(r, g, b, 255), IsAntialias = false })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRect(new SKRect(0, 0, 64, 64), paint);
        }

        using var solid = new Bitmap(skBitmap);
        using Bitmap converted = solid.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

        // Dithering a flat fill alternates between the two levels bracketing the F16-rounded value,
        // so it must never stray further than one level from the requested color.
        ReadOnlySpan<byte> pixels = converted.GetPixelSpan();
        for (int i = 0; i < converted.Width * converted.Height; i++)
        {
            Assert.That(pixels[i * 4 + 2], Is.EqualTo(r).Within(1), $"red at pixel {i}");
            Assert.That(pixels[i * 4 + 1], Is.EqualTo(g).Within(1), $"green at pixel {i}");
            Assert.That(pixels[i * 4], Is.EqualTo(b).Within(1), $"blue at pixel {i}");
        }
    }

    [Test]
    public void Convert_ToAlpha8_PreservesTheContourMask()
    {
        // ContourTracer converts to Alpha8 and thresholds it at `alpha > 0`, so a dither that nudged
        // a near-zero alpha across that boundary would silently reshape traced geometry.
        var info = new SKImageInfo(256, 128, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        using (var shape = new SKPaint { Color = SKColors.White, IsAntialias = true })
        using (var faint = new SKPaint { Color = new SKColor(255, 255, 255, 1), IsAntialias = false })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawOval(new SKRect(16, 16, 240, 112), shape);
            canvas.DrawRect(new SKRect(0, 0, 256, 8), faint);
        }

        using var source = new Bitmap(skBitmap);
        using Bitmap alpha = source.Convert(BitmapColorType.Alpha8);

        var expectedInfo = new SKImageInfo(256, 128, SKColorType.Alpha8, SKAlphaType.Premul);
        using var expected = new SKBitmap(expectedInfo);
        using (var canvas = new SKCanvas(expected))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src, IsDither = false })
        {
            canvas.DrawBitmap(skBitmap, SKPoint.Empty, paint);
        }

        ReadOnlySpan<byte> actualPixels = alpha.GetPixelSpan();
        ReadOnlySpan<byte> expectedPixels = expected.GetPixelSpan();
        for (int i = 0; i < expectedPixels.Length; i++)
        {
            Assert.That(actualPixels[i] > 0, Is.EqualTo(expectedPixels[i] > 0),
                $"foreground mask flipped at pixel {i}");
        }
    }

    [TestCase(BitmapColorType.RgbaF16, true)]
    [TestCase(BitmapColorType.RgbaF32, true)]
    [TestCase(BitmapColorType.Rgba16161616, false)]
    public void Convert_WithoutPrecisionLoss_IsUnaffectedByDither(BitmapColorType colorType, bool linear)
    {
        using Bitmap ramp = CreateLinearRamp();
        using Bitmap converted = ramp.Convert(colorType, BitmapAlphaType.Premul,
            linear ? BitmapColorSpace.LinearSrgb : BitmapColorSpace.Srgb);

        // Skia only perturbs pixels the destination cannot represent, so a conversion that keeps or
        // gains precision must stay smooth rather than pick up dither noise.
        ReadOnlySpan<byte> row0 = converted.GetRow(0);
        for (int y = 1; y < converted.Height; y++)
        {
            Assert.That(converted.GetRow(y).SequenceEqual(row0), Is.True,
                $"row {y} differs from row 0, so dither leaked into a lossless conversion");
        }
    }
}
