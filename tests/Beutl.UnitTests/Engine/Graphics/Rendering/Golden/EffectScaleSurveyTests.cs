using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Empirical FR-009 survey (T027): how faithfully each effect survives a 0.5x render upscaled back to full,
// revealing which effects are already CTM-correct vs need buffer activation.
[NonParallelizable]
[TestFixture]
public class EffectScaleSurveyTests
{
    private static readonly PixelSize Frame = new(250, 250);

    public static IEnumerable<TestCaseData> Effects()
    {
        yield return new TestCaseData("Blur", (Func<FilterEffect>)(() =>
        {
            var e = new Blur(); e.Sigma.CurrentValue = new Size(8, 8); return e;
        }));
        yield return new TestCaseData("DropShadow", (Func<FilterEffect>)(() =>
        {
            var e = new DropShadow();
            e.Position.CurrentValue = new Point(12, 12);
            e.Sigma.CurrentValue = new Size(6, 6);
            e.Color.CurrentValue = Colors.Black;
            return e;
        }));
        yield return new TestCaseData("InnerShadow", (Func<FilterEffect>)(() =>
        {
            var e = new InnerShadow();
            e.Position.CurrentValue = new Point(6, 6);
            e.Sigma.CurrentValue = new Size(6, 6);
            e.Color.CurrentValue = Colors.Black;
            return e;
        }));
        yield return new TestCaseData("Brightness", (Func<FilterEffect>)(() =>
        {
            var e = new Brightness(); e.Amount.CurrentValue = 50f; return e;
        }));
        yield return new TestCaseData("Dilate", (Func<FilterEffect>)(() =>
        {
            var e = new Dilate(); e.RadiusX.CurrentValue = 3f; e.RadiusY.CurrentValue = 3f; return e;
        }));
        yield return new TestCaseData("Mosaic", (Func<FilterEffect>)(() =>
        {
            var e = new MosaicEffect(); e.TileSize.CurrentValue = new Size(12, 12); return e;
        }));
    }

    [TestCaseSource(nameof(Effects))]
    public void ReducedScaleFidelity(string name, Func<FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Drawable.Resource Make()
            {
                var shape = new EllipseShape();
                shape.AlignmentX.CurrentValue = AlignmentX.Center;
                shape.AlignmentY.CurrentValue = AlignmentY.Center;
                shape.TransformOrigin.CurrentValue = RelativePoint.Center;
                shape.Width.CurrentValue = 150;
                shape.Height.CurrentValue = 110;
                shape.Fill.CurrentValue = Brushes.White;
                shape.FilterEffect.CurrentValue = makeEffect();
                return shape.ToResource(CompositionContext.Default);
            }

            using Bitmap full = GoldenImageHarness.RenderAtScale(Make(), Frame, 1f);
            using Bitmap half = GoldenImageHarness.RenderAtScale(Make(), Frame, 0.5f);
            using Bitmap upscaled = GoldenImageHarness.MitchellResampleTo(half, new PixelSize(full.Width, full.Height));
            double ssim = ImageMetrics.Ssim(full, upscaled);
            double mae = ImageMetrics.MeanAbsoluteError(full, upscaled);
            TestContext.WriteLine($"[{name}] reduced-scale SSIM={ssim:F4} MAE={mae:F4}");

            Assert.That(ssim, Is.GreaterThan(0.95), $"{name} SSIM={ssim:F4}");
        });
    }
}
