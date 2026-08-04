using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Every effect that paints with a user-settable brush or pen must register it so nested DrawableBrush content is
// lowered into the recorded graph. A registered drawable brush whose content is a flat colour must therefore
// render like the equivalent solid brush, and must not render like an absent brush.
[NonParallelizable]
[TestFixture]
public class EffectDrawableBrushLoweringTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private const double EquivalenceTolerance = 0.02;

    public static IEnumerable<TestCaseData> Effects()
    {
        yield return new TestCaseData(
            "FlatShadow",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new FlatShadow();
                effect.Angle.CurrentValue = 0;
                effect.Length.CurrentValue = 40;
                effect.Brush.CurrentValue = brush;
                return effect;
            }));
        yield return new TestCaseData(
            "BlendEffect",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new BlendEffect();
                effect.Brush.CurrentValue = brush;
                effect.BlendMode.CurrentValue = BlendMode.SrcIn;
                return effect;
            }));
        yield return new TestCaseData(
            "StrokeEffect",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new StrokeEffect();
                if (brush is not null)
                {
                    var pen = new Pen();
                    pen.Thickness.CurrentValue = 14;
                    pen.Brush.CurrentValue = brush;
                    effect.Pen.CurrentValue = pen;
                }

                return effect;
            }));
        yield return new TestCaseData(
            "DisplacementMap-ShowMap",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new DisplacementMapEffect();
                effect.DisplacementMap.CurrentValue = brush;
                effect.ShowDisplacementMap.CurrentValue = true;
                return effect;
            }));
    }

    [TestCaseSource(nameof(Effects))]
    public void EffectOwnedDrawableBrush_RendersLikeTheEquivalentSolidBrush(
        string name,
        Func<Brush?, FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap solid = GoldenImageHarness.RenderAtScale(
                Make(() => makeEffect(MakeSolid())),
                Frame,
                1f);
            using Bitmap drawable = GoldenImageHarness.RenderAtScale(
                Make(() => makeEffect(MakeDrawableBrush())),
                Frame,
                1f);
            using Bitmap absent = GoldenImageHarness.RenderAtScale(
                Make(() => makeEffect(null)),
                Frame,
                1f);

            Assert.That(
                ImageMetrics.FirstNonFinite(
                    ("solid", solid),
                    ("drawable", drawable),
                    ("absent", absent)),
                Is.Null,
                $"{name}: the drawable-brush comparison requires finite renders");

            double equivalence = ImageMetrics.MeanAbsoluteError(solid, drawable);
            double vacuity = ImageMetrics.MeanAbsoluteError(absent, drawable);
            TestContext.WriteLine(
                $"[{name}] drawable vs solid MAE={equivalence:F4}, drawable vs absent MAE={vacuity:F4}");

            Assert.That(
                vacuity,
                Is.GreaterThan(0.001),
                $"{name}: the effect-owned DrawableBrush rendered like an absent brush, so its nested content "
                + "was never materialized");
            Assert.That(
                equivalence,
                Is.LessThan(EquivalenceTolerance),
                $"{name}: the effect-owned DrawableBrush did not render like the equivalent solid brush");
        });
    }

    private static Brush MakeSolid()
    {
        var brush = new SolidColorBrush();
        brush.Color.CurrentValue = Colors.Red;
        return brush;
    }

    private static Brush MakeDrawableBrush()
    {
        var content = new RectShape();
        content.AlignmentX.CurrentValue = AlignmentX.Center;
        content.AlignmentY.CurrentValue = AlignmentY.Center;
        content.Width.CurrentValue = 200;
        content.Height.CurrentValue = 200;
        content.Fill.CurrentValue = MakeSolid();
        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = content;
        brush.Stretch.CurrentValue = Stretch.Fill;
        // A stroke paints outside the brush frame, where TileMode.None decals to transparent; tiling keeps the
        // comparison against a solid brush apples-to-apples.
        brush.TileMode.CurrentValue = TileMode.Tile;
        return brush;
    }

    private static Drawable.Resource Make(Func<FilterEffect> makeEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;
        shape.FilterEffect.CurrentValue = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }
}
