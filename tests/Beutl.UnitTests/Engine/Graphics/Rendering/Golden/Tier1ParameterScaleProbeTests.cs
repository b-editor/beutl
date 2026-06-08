using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Empirical Tier-1 findings (2026-06-09): does a logical-unit parameter survive a 0.5x reduced render
// upscaled back to full? A LOW SSIM means the parameter is device-coupled and needs ×w / ÷w; a HIGH
// SSIM means it is already scale-correct (operates in logical space) and must NOT be re-scaled (that
// would double-scale it). These probes settle the stale "deferred ×w" notes empirically:
//   - ShakeEffect:      SSIM ~0.999 -> ALREADY scale-correct (logical-bounds translate). Do NOT ×w.
//   - PerlinNoiseBrush: SSIM ~0.70  -> device-coupled (SkPerlinNoiseShader generates in device space and
//                       does not follow the CTM); BaseFrequency ÷w is genuinely needed. The fix requires
//                       threading the render/working scale into BrushConstructor (which today is scale-blind),
//                       a change shared with the audio-visualizer + tile-brush density work -> Tier 2, not 1.
[NonParallelizable]
[TestFixture]
public class Tier1ParameterScaleProbeTests
{
    private static readonly PixelSize Frame = new(250, 250);

    // PerlinNoiseBrush fill: SKShader.CreatePerlinNoise* generates noise in a coordinate space whose
    // resolution-dependence is a Skia internal. This probe settles whether BaseFrequency needs ÷w.
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
            // Device-coupled today (BaseFrequency ÷w not yet wired — needs BrushConstructor scale, Tier 2).
            // Loose floor catches a gross regression; raise to the exact gate once ÷w lands.
            Assert.That(ssim, Is.GreaterThan(0.6),
                $"PerlinNoiseBrush SSIM={ssim:F4} (device-coupled; BaseFrequency ÷w pending — Tier 2)");
        });
    }

    // ShakeEffect.StrengthX/Y translate the effect target's LOGICAL bounds; if the pipeline scales logical
    // bounds to device by ×w at allocation, a logical translate is already scale-correct and must NOT be ×w'd.
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
