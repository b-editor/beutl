using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A-1 / T042 — measured impact + guard. A DrawableBrush FILL rasterizes its content into a LOGICAL-sized
// intermediate, then the tile shader samples it under the root CreateScale(s) CTM, so at s_out > 1 (SSAA) the
// fill is upscaled from a logical-res intermediate rather than re-rendered at device density.
//
// MEASURED (2026-06-09): for typical (smooth) content the impact is negligible — a DrawableBrush-filled
// ellipse at 2× SSAA scores SSIM 0.998 vs the ellipse drawn directly (the edge softness averages out). It
// would only matter for high-frequency FILL content at large SSAA factors.
//
// A density fix (size the intermediate ceil(IntermediateSize×s), re-render the child at ×s, and compensate
// the tile shader's local-matrix by Scale(s)) was ATTEMPTED and reverted — the multi-scale matrix composition
// in Skia shader space is subtle and the naive version dropped this very test to SSIM 0.21 (mistiled). A
// correct fix needs a dedicated debugging loop across TileMode/Stretch/Transform with per-mode goldens; the
// Tier-3 deferral is the right call. This test stays as the impact characterisation + a regression guard
// (it must not drop below the smooth-content baseline).
[NonParallelizable]
[TestFixture]
public class TileBrushFillDensityTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static EllipseShape MakeEllipse()
    {
        var e = new EllipseShape();
        e.AlignmentX.CurrentValue = AlignmentX.Center;
        e.AlignmentY.CurrentValue = AlignmentY.Center;
        e.Width.CurrentValue = 160;
        e.Height.CurrentValue = 160;
        e.Fill.CurrentValue = Brushes.White;
        return e;
    }

    private static Drawable.Resource MakeDirect() => MakeEllipse().ToResource(CompositionContext.Default);

    private static Drawable.Resource MakeBrushFilled()
    {
        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = MakeEllipse();
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.None;

        var rect = new RectShape();
        rect.AlignmentX.CurrentValue = AlignmentX.Center;
        rect.AlignmentY.CurrentValue = AlignmentY.Center;
        rect.Width.CurrentValue = 160;
        rect.Height.CurrentValue = 160;
        rect.Fill.CurrentValue = brush;
        return rect.ToResource(CompositionContext.Default);
    }

    [Test]
    public void DrawableBrushFill_SsaaDensity_MatchesDirect()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap direct = GoldenImageHarness.RenderAtScale(MakeDirect(), Frame, 2f);
            using Bitmap brush = GoldenImageHarness.RenderAtScale(MakeBrushFilled(), Frame, 2f);
            double ssim = ImageMetrics.Ssim(direct, brush);
            double mae = ImageMetrics.MeanAbsoluteError(direct, brush);
            TestContext.WriteLine($"[DrawableBrush fill @2x] vs direct SSIM={ssim:F4} MAE={mae:F4}");
            Assert.That(ssim, Is.GreaterThan(GoldenThresholds.ExactSsimMin),
                $"DrawableBrush fill is softer than direct at SSAA (density-capped): SSIM={ssim:F4}");
        });
    }
}
