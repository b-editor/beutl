using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Vulkan-gated golden harness for feature 003. On a host with MoltenVK/SwiftShader these run for
// real; otherwise VulkanTestEnvironment.EnsureAvailable() skips them. All GPU work is marshalled
// onto the render thread.
internal static class GoldenImageHarness
{
    /// <summary>
    /// Renders <paramref name="resource"/> into a <c>ceil(logicalSize × scale)</c> device surface with
    /// one root <c>CreateScale(scale)</c> CTM — exactly the model <see cref="Renderer.Render"/> uses.
    /// <c>scale == 1</c> takes the bare path (byte-identity).
    /// </summary>
    public static Bitmap RenderAtScale(Drawable.Resource resource, PixelSize logicalSize, float scale)
    {
        int dw = (int)MathF.Ceiling(logicalSize.Width * scale);
        int dh = (int)MathF.Ceiling(logicalSize.Height * scale);
        using RenderTarget target = RenderTarget.Create(dw, dh)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target) { OutputScale = scale };
        canvas.Clear(Colors.Black);

        // Mirror Renderer.RenderDrawable exactly: layout uses the LOGICAL frame size (so alignment /
        // centering is scale-independent); the root CreateScale(scale) CTM maps it onto the device surface.
        using var node = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(node, logicalSize, scale))
        {
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, outputScale: scale);
        RenderNodeOperation[] ops = processor.PullToRoot();

        void RenderOps()
        {
            foreach (RenderNodeOperation op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }
        }

        if (scale == 1f)
        {
            RenderOps();
        }
        else
        {
            using (canvas.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                RenderOps();
            }
        }

        return target.Snapshot();
    }

    /// <summary>Mitchell-resamples an RgbaF16 bitmap to <paramref name="size"/>.</summary>
    public static Bitmap MitchellResampleTo(Bitmap src, PixelSize size)
    {
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.RgbaF16, SKAlphaType.Premul,
            src.SKBitmap.ColorSpace);
        var dst = new SKBitmap(info);
        using (var canvas = new SKCanvas(dst))
        using (SKImage img = SKImage.FromBitmap(src.SKBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(img, new SKRect(0, 0, size.Width, size.Height),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        return new Bitmap(dst);
    }

    /// <summary>Asserts two bitmaps are exactly equal byte-for-byte.</summary>
    public static void AssertByteIdentical(Bitmap expected, Bitmap actual)
    {
        Assert.That(actual.Width, Is.EqualTo(expected.Width), "width");
        Assert.That(actual.Height, Is.EqualTo(expected.Height), "height");
        ReadOnlySpan<byte> a = expected.GetPixelSpan();
        ReadOnlySpan<byte> b = actual.GetPixelSpan();
        Assert.That(b.SequenceEqual(a), Is.True, "pixel bytes are not identical");
    }

    /// <summary>
    /// Asserts a reduced-scale render, upscaled back to the reference size, structurally matches the
    /// full-scale reference (SSIM ≥ <see cref="GoldenThresholds.ExactSsimMin"/>, MAE ≤ ExactMaeMax).
    /// </summary>
    public static void AssertReducedScaleExact(Bitmap fullScale, Bitmap reduced)
    {
        using Bitmap upscaled = MitchellResampleTo(reduced, new PixelSize(fullScale.Width, fullScale.Height));
        double ssim = ImageMetrics.Ssim(fullScale, upscaled);
        double mae = ImageMetrics.MeanAbsoluteError(fullScale, upscaled);
        TestContext.WriteLine($"reduced-scale SSIM={ssim:F4} MAE={mae:F4}");
        Assert.Multiple(() =>
        {
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), "SSIM");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), "MAE");
        });
    }

    /// <summary>
    /// Asserts a supersampled render (scale &gt; 1) downsampled to the reference size has LESS
    /// aliasing energy than the 1:1 render (SC-009 export SSAA).
    /// </summary>
    public static void AssertSupersampleReducesAliasing(Bitmap oneToOne, Bitmap supersampledDownsampled)
    {
        double baseEnergy = ImageMetrics.AliasingEnergy(oneToOne);
        double ssEnergy = ImageMetrics.AliasingEnergy(supersampledDownsampled);
        TestContext.WriteLine($"aliasing energy: 1:1={baseEnergy:F5} supersampled={ssEnergy:F5}");
        Assert.That(ssEnergy, Is.LessThanOrEqualTo(baseEnergy + 1e-6),
            "supersampling did not reduce aliasing energy");
    }
}
