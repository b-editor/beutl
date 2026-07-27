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

    /// <summary>A 2px checkerboard: nearest sampling keeps it pure black and white, linear averages it.</summary>
    private static Bitmap CreateCheckerboard()
    {
        var info = new SKImageInfo(Width, Width, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = false })
        {
            canvas.Clear(SKColors.Black);
            for (int y = 0; y < Width; y += 2)
            {
                for (int x = (y / 2 % 2 == 0 ? 0 : 2); x < Width; x += 4)
                {
                    canvas.DrawRect(new SKRect(x, y, x + 2, y + 2), paint);
                }
            }
        }

        return new Bitmap(skBitmap);
    }

    [Test]
    public void RoundTrip_Downscaled_ResamplesLinearlyRatherThanNearest()
    {
        using var manager = new FrameCacheManager(
            new PixelSize(Width, Width),
            new FrameCacheOptions(FrameCacheScale.Manual, FrameCacheColorType.BGRA)
            {
                Size = new PixelSize(Width / 4, Width / 4)
            })
        { IsEnabled = true };

        using (Ref<Bitmap> source = Ref<Bitmap>.Create(CreateCheckerboard()))
        {
            manager.Add(0, source);
        }

        Assert.That(manager.TryGet(0, out Ref<Bitmap>? cached), Is.True);
        using (cached)
        {
            Bitmap bitmap = cached!.Value;
            Assert.That(bitmap.Width, Is.EqualTo(Width / 4));
            Assert.That(bitmap.Height, Is.EqualTo(Width / 4));

            // The checkerboard is exactly half white, so a linear average lands on 0.5 in the linear
            // space the frames are rendered in, which is 187.5 once encoded to 8-bit sRGB. Nearest
            // sampling would instead land on whole source texels and return 0 or 255. The tolerance
            // covers the dither, which moves each pixel to one of the two levels bracketing 187.5.
            const double ExpectedLevel = 187.5;
            ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
            int bpp = bitmap.BytesPerPixel;
            for (int i = 0; i < bitmap.Width * bitmap.Height; i++)
            {
                Assert.That((double)pixels[i * bpp], Is.EqualTo(ExpectedLevel).Within(1.0),
                    $"pixel {i} is not the linear blend of the checkerboard");
            }
        }
    }
}
