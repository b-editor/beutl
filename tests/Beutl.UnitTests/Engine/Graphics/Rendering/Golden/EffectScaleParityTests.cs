using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// feature 003: every render-target ("Custom") effect with an absolute-length pixel parameter must scale that
// parameter by the working density, so the supersampled (s_out = 2) result keeps the SAME logical appearance as
// the 1:1 render. SSIM(1:1, 2x-delivered) > 0.95 = scale-correct; a large drop = an un-scaled device-density
// parameter (the DisplacementMap-class bug). White-filled rotated rect: the effect operates on the rect's edges /
// interior tiling, which is high-frequency enough to expose a wrong-density parameter.
[NonParallelizable]
public class EffectScaleParityTests
{
    private static readonly PixelSize Frame = new(200, 200);

    public static IEnumerable<TestCaseData> Effects()
    {
        yield return new TestCaseData("InnerShadow", (Func<FilterEffect>)(() =>
        {
            var e = new InnerShadow();
            e.Position.CurrentValue = new Point(20, 20);
            e.Sigma.CurrentValue = new Size(10, 10);
            e.Color.CurrentValue = Colors.Black;
            return e;
        }));
        yield return new TestCaseData("ColorShift", (Func<FilterEffect>)(() =>
        {
            var e = new ColorShift();
            e.RedOffset.CurrentValue = new PixelPoint(18, 6);
            e.BlueOffset.CurrentValue = new PixelPoint(-18, -6);
            return e;
        }));
        yield return new TestCaseData("SplitEffect", (Func<FilterEffect>)(() =>
        {
            var e = new SplitEffect();
            e.HorizontalDivisions.CurrentValue = 3;
            e.VerticalDivisions.CurrentValue = 3;
            e.HorizontalSpacing.CurrentValue = 12;
            e.VerticalSpacing.CurrentValue = 12;
            return e;
        }));
        yield return new TestCaseData("FlatShadow", (Func<FilterEffect>)(() =>
        {
            var e = new FlatShadow();
            e.Angle.CurrentValue = 0;
            e.Length.CurrentValue = 40;
            e.Brush.CurrentValue = Brushes.Red;
            return e;
        }));
        yield return new TestCaseData("Clipping", (Func<FilterEffect>)(() =>
        {
            var e = new Clipping();
            e.Left.CurrentValue = 24;
            e.Top.CurrentValue = 24;
            e.Right.CurrentValue = 24;
            e.Bottom.CurrentValue = 24;
            return e;
        }));
        yield return new TestCaseData("Clipping-AutoCenter", (Func<FilterEffect>)(() =>
        {
            var e = new Clipping();
            e.Left.CurrentValue = 20;
            e.Top.CurrentValue = 20;
            e.Right.CurrentValue = 20;
            e.Bottom.CurrentValue = 20;
            e.AutoCenter.CurrentValue = true;
            return e;
        }));
        yield return new TestCaseData("StrokeEffect", (Func<FilterEffect>)(() =>
        {
            var pen = new Pen();
            pen.Thickness.CurrentValue = 14;
            pen.Brush.CurrentValue = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.CurrentValue = pen;
            return e;
        }));
        yield return new TestCaseData("PathFollow", (Func<FilterEffect>)(() =>
        {
            var e = new PathFollowEffect();
            e.Geometry.CurrentValue = PathGeometry.Parse("M0,0 L70,50");
            e.Progress.CurrentValue = 100;
            return e;
        }));
    }

    private static Drawable.Resource Make(Func<FilterEffect> makeEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 21f;
        shape.Transform.CurrentValue = rotation;
        shape.FilterEffect.CurrentValue = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }

    [TestCaseSource(nameof(Effects))]
    public void Effect_Supersampled_KeepsLogicalAppearance(string name, Func<FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap r1 = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 1f);
            using Bitmap hi = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);
            double ssim = ImageMetrics.Ssim(r1, delivered);
            TestContext.WriteLine($"[{name}] 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                $"{name}: supersampled diverged from 1:1 — an absolute-px parameter is not scaled by the working density");
        });
    }
}
