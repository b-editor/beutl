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
//   - PerlinNoiseBrush: SSIM ~0.70  -> this is NOT a frequency mismatch but the inherent best-effort
//                       downsampling loss of a high-frequency procedural texture (FR-013). The "BaseFrequency
//                       ÷w" recommendation in the dossier was tried as a one-off A/B in commit 8b2a1624c and
//                       made the reduced-scale result WORSE (0.70 -> 0.63 at 0.5x) — SkPerlinNoiseShader
//                       already follows the CTM (the noise period is logical-invariant), so ÷w only adds
//                       higher frequencies that downsampling then loses. That experiment was reverted and is
//                       NOT reproduced by the assertion below (the ÷w variant is no longer plumbed); the
//                       falsification lives in git history (8b2a1624c). PerlinNoise ships with NO param
//                       scaling, and 0.70 is accepted best-effort. The loose floor below is a REGRESSION
//                       FLOOR ONLY — it cannot distinguish the ÷w hypothesis (both 0.70 and 0.63 clear it).
//                       The CTM-following hypothesis itself (FR-010) IS settled by the lossless-direction
//                       probe (PerlinNoiseBrush_CtmFollowing_LosslessDirection), which is free of the
//                       Nyquist confound.
[NonParallelizable]
[TestFixture]
public class Tier1ParameterScaleProbeTests
{
    private static readonly PixelSize Frame = new(250, 250);

    // FR-010, lossless direction: the 0.5x probe below cannot separate "shader follows the CTM" from
    // "shader is device-fixed" because high-frequency content also loses structure to Nyquist at 0.5x. This
    // probe removes the confound: render at s_out = 2.0 (information is GAINED, not lost), downscale to the
    // 1x size, and compare with the 1x render, using a BaseFrequency far below Nyquist at 1x (~0.015
    // cycles/px ≈ 3.75 cycles across the 250px frame). If SkPerlinNoiseShader follows the CTM (the FR-010
    // position), the logical noise structure is identical at both scales and survives the downscale; if it
    // were device-fixed, the 2x pattern would be twice the logical period and the comparison would collapse.
    // This verifies the CTM-following hypothesis only in the lossless direction — it says nothing about how
    // much detail a REDUCED-scale render keeps (that stays best-effort, probed above).
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
            // Best-effort by nature (FR-013); BaseFrequency ÷w was tried and made it WORSE (see class note),
            // so no param scaling is applied. Loose floor catches only a gross regression.
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
