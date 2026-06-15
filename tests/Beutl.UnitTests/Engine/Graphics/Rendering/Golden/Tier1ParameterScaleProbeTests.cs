using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Empirical Tier-1 findings (2026-06-09): does a logical-unit parameter survive a 0.5x reduced render
// upscaled back to full? LOW SSIM means the parameter is device-coupled and needs ×w / ÷w; HIGH SSIM
// means it is already scale-correct (logical space) and must NOT be re-scaled (that double-scales it):
//   - ShakeEffect:      SSIM ~0.999 -> ALREADY scale-correct (logical-bounds translate). Do NOT ×w.
//   - PerlinNoiseBrush: SSIM ~0.70  -> not a frequency mismatch but the inherent best-effort downsampling
//                       loss of a high-frequency procedural texture (FR-013). The "BaseFrequency ÷w"
//                       recommendation was A/B-tested in commit 8b2a1624c and made the 0.5x result WORSE
//                       (0.70 -> 0.63): SkPerlinNoiseShader already follows the CTM (noise period is
//                       logical-invariant), so ÷w only adds higher frequencies that downsampling loses.
//                       That ÷w variant was reverted and is no longer plumbed (falsification in git history,
//                       8b2a1624c). PerlinNoise ships with NO param scaling; 0.70 is accepted best-effort.
//                       The loose floor below is a REGRESSION FLOOR ONLY — both 0.70 and 0.63 clear it, so
//                       it cannot distinguish the ÷w hypothesis. CTM-following itself (FR-010) is settled by
//                       the lossless-direction probe (PerlinNoiseBrush_CtmFollowing_LosslessDirection),
//                       which is free of the Nyquist confound.
[NonParallelizable]
[TestFixture]
public class Tier1ParameterScaleProbeTests
{
    private static readonly PixelSize Frame = new(250, 250);

    // FR-010, lossless direction: the 0.5x probe below cannot separate "shader follows the CTM" from
    // "shader is device-fixed" because high-frequency content also loses structure to Nyquist at 0.5x. This
    // probe removes the confound by rendering at s_out = 2.0 (information GAINED, not lost), downscaling to
    // 1x, and comparing with the 1x render, using a BaseFrequency far below Nyquist at 1x (~0.015 cycles/px
    // ≈ 3.75 cycles across the 250px frame). If the shader follows the CTM (FR-010), the logical noise
    // structure is identical at both scales and survives the downscale; if it were device-fixed, the 2x
    // pattern would be twice the logical period and the comparison would collapse. This verifies CTM-following
    // only in the lossless direction — it says nothing about detail kept by a REDUCED-scale render (best-effort,
    // probed above).
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

    // PerlinNoiseBrush fill at high frequency: probes whether BaseFrequency needs ÷w (see class note).
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
            // Best-effort (FR-013); ÷w made it WORSE (see class note), so no param scaling. Loose floor
            // catches only a gross regression.
            Assert.That(ssim, Is.GreaterThan(0.6),
                $"PerlinNoiseBrush SSIM={ssim:F4} (best-effort procedural texture; ÷w empirically does not help)");
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
