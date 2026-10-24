using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class TextDrawableBrushTests
{
    private static readonly PixelSize Frame = new(480, 160);

    [TestCase(0.5f, false, false)]
    [TestCase(1f, false, false)]
    [TestCase(2f, false, false)]
    [TestCase(0.5f, true, false)]
    [TestCase(1f, true, false)]
    [TestCase(2f, true, false)]
    [TestCase(1f, false, true)]
    [TestCase(1f, true, true)]
    public void DrawableFill_MatchesEquivalentBrush(float scale, bool splitByCharacters, bool rotatedGradient)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Brush reference = rotatedGradient ? CreateGradient() : Brushes.Red;
            DrawableBrush brush = CreateBrush(rotatedGradient ? CreateGradient() : Brushes.Red);
            if (rotatedGradient)
            {
                // Rotation must use the glyph bounds' center, including their negative baseline offset.
                reference.Transform.Value = new RotationTransform(180);
                brush.Transform.Value = new RotationTransform(180);
            }

            using Drawable.Resource referenceText = CreateText(reference, splitByCharacters);
            using Drawable.Resource drawableText = CreateText(brush, splitByCharacters);
            using Bitmap expected = GoldenImageHarness.RenderAtScale(
                referenceText, Frame, scale, clearColor: Colors.Transparent);
            using Bitmap actual = GoldenImageHarness.RenderAtScale(
                drawableText, Frame, scale, clearColor: Colors.Transparent);

            double error = ImageMetrics.MeanAbsoluteError(expected, actual);
            TestContext.WriteLine($"Text drawable fill at {scale}x, split={splitByCharacters}: MAE={error:F6}");
            Assert.That(ImageMetrics.HasFiniteVisibleContent(expected), Is.True);
            Assert.That(ImageMetrics.HasFiniteVisibleContent(actual), Is.True);
            Assert.That(error, Is.LessThan(0.001),
                "A non-tiling drawable fill must cover the glyphs above and below their baseline.");
        });
    }

    private static LinearGradientBrush CreateGradient()
    {
        var gradient = new LinearGradientBrush();
        gradient.StartPoint.Value = new RelativePoint(0, 0, RelativeUnit.Relative);
        gradient.EndPoint.Value = new RelativePoint(0, 1, RelativeUnit.Relative);
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        return gradient;
    }

    private static DrawableBrush CreateBrush(Brush fill)
    {
        var content = new RectShape();
        content.Width.Value = 64;
        content.Height.Value = 64;
        content.Fill.Value = fill;
        var brush = new DrawableBrush(content);
        brush.Stretch.Value = Stretch.Fill;
        brush.TileMode.Value = TileMode.None;
        return brush;
    }

    private static Drawable.Resource CreateText(Brush fill, bool splitByCharacters)
    {
        var text = new TextBlock();
        text.FontFamily.Value = TypefaceProvider.Typeface().FontFamily;
        text.Size.Value = 64;
        text.Text.Value = "Beutl gyp";
        text.AlignmentX.Value = AlignmentX.Center;
        text.AlignmentY.Value = AlignmentY.Center;
        text.SplitByCharacters.Value = splitByCharacters;
        text.Fill.Value = fill;
        return text.ToResource(CompositionContext.Default);
    }
}
