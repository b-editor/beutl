using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Tier-1 parameter scale probes: does a logical-unit parameter survive a 0.5x reduced render?
//   - ShakeEffect: scale-correct (logical-bounds translate), do not scale.
//   - PerlinNoiseBrush: ~0.70 SSIM is best-effort downsampling loss, not a scaling defect.
[NonParallelizable]
[TestFixture]
public class Tier1ParameterScaleProbeTests
{
    private static readonly PixelSize Frame = new(250, 250);

    // Lossless direction: render at 2.0, downscale to 1x. Verifies the shader follows the CTM
    // (logical noise structure matches) without the Nyquist confound of the 0.5x probe.
    [Test]
    public void PerlinNoiseBrush_CtmFollowing_LosslessDirection()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Drawable.Resource Make()
            {
                var brush = new PerlinNoiseBrush();
                brush.BaseFrequencyX.CurrentValue = 1.5f; // 0.015 cycles/unit after the /100 — far from Nyquist at 1x
                brush.BaseFrequencyY.CurrentValue = 1.5f;
                brush.Octaves.CurrentValue = 2;
                brush.Seed.CurrentValue = 1f;

                var shape = new RectShape();
                shape.AlignmentX.CurrentValue = AlignmentX.Center;
                shape.AlignmentY.CurrentValue = AlignmentY.Center;
                shape.Width.CurrentValue = 180;
                shape.Height.CurrentValue = 180;
                shape.Fill.CurrentValue = brush;
                return shape.ToResource(CompositionContext.Default);
            }

            using Bitmap full = GoldenImageHarness.RenderAtScale(Make(), Frame, 1f);
            using Bitmap doubled = GoldenImageHarness.RenderAtScale(Make(), Frame, 2f);
            using Bitmap downscaled = GoldenImageHarness.MitchellResampleTo(doubled, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, downscaled);
            double mae = ImageMetrics.MeanAbsoluteError(full, downscaled);
            TestContext.WriteLine($"[PerlinNoiseBrush lossless] 2.0-down vs 1.0 SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(0.9),
                $"PerlinNoiseBrush SSIM={ssim:F4} — low-frequency noise structure did not survive the lossless " +
                "(2.0 → 1.0) direction, so the shader is NOT following the CTM (FR-010 violated)");
        });
    }

    // PerlinNoiseBrush fill at high frequency: probes whether BaseFrequency needs /w.
    [Test]
    public void PerlinNoiseBrush_FillFidelity_AcrossScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Drawable.Resource Make()
            {
                var brush = new PerlinNoiseBrush();
                brush.BaseFrequencyX.CurrentValue = 20f; // 0.20 cycles/unit after the /100 in CreatePerlinNoiseShader
                brush.BaseFrequencyY.CurrentValue = 20f;
                brush.Octaves.CurrentValue = 3;
                brush.Seed.CurrentValue = 1f;

                var shape = new RectShape();
                shape.AlignmentX.CurrentValue = AlignmentX.Center;
                shape.AlignmentY.CurrentValue = AlignmentY.Center;
                shape.Width.CurrentValue = 180;
                shape.Height.CurrentValue = 180;
                shape.Fill.CurrentValue = brush;
                return shape.ToResource(CompositionContext.Default);
            }

            using Bitmap full = GoldenImageHarness.RenderAtScale(Make(), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(Make(), Frame, 0.5f);
            using Bitmap upscaled = GoldenImageHarness.MitchellResampleTo(half, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, upscaled);
            double mae = ImageMetrics.MeanAbsoluteError(full, upscaled);
            TestContext.WriteLine($"[PerlinNoiseBrush] reduced-scale SSIM={ssim:F4} MAE={mae:F4}");
            // Best-effort; /w made it worse. Loose floor catches only a gross regression.
            Assert.That(ssim, Is.GreaterThan(0.6),
                $"PerlinNoiseBrush SSIM={ssim:F4} (best-effort procedural texture; ÷w empirically does not help)");
        });
    }

    // ShakeEffect.StrengthX/Y translate logical bounds; already scale-correct, must not be scaled again.
    [Test]
    public void ShakeEffect_LogicalDisplacement_AcrossScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // One shared resource rendered at both scales, so the random per-instance shake offset is identical.
            var shape = new RectShape();
            shape.AlignmentX.CurrentValue = AlignmentX.Center;
            shape.AlignmentY.CurrentValue = AlignmentY.Center;
            shape.Width.CurrentValue = 120;
            shape.Height.CurrentValue = 120;
            shape.Fill.CurrentValue = Brushes.White;
            var shake = new Beutl.Graphics.Effects.ShakeEffect();
            shake.StrengthX.CurrentValue = 30f;
            shake.StrengthY.CurrentValue = 30f;
            shape.FilterEffect.CurrentValue = shake;
            Drawable.Resource res = shape.ToResource(CompositionContext.Default);

            using Bitmap full = GoldenImageHarness.RenderAtScale(res, Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(res, Frame, 0.5f);
            using Bitmap upscaled = GoldenImageHarness.MitchellResampleTo(half, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, upscaled);
            double mae = ImageMetrics.MeanAbsoluteError(full, upscaled);
            TestContext.WriteLine($"[ShakeEffect] reduced-scale SSIM={ssim:F4} MAE={mae:F4}");
            // ShakeEffect is already scale-correct (logical-bounds translate); gate it as an "exact" effect.
            // A regression here (SSIM drop) would flag someone wrongly ×w-ing Strength (double-scaling it).
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"ShakeEffect SSIM={ssim:F4} — Strength must stay logical (do not ×w)");
        });
    }
}
