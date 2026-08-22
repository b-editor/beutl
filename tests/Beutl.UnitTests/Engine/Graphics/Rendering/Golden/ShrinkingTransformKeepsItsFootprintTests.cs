using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// A scope bounds its callback to its own footprint, and that bound is stated in the callback's local
/// units. Replayed under a shrinking transform those units cover a sub-pixel span, which a
/// non-antialiased clip snaps to the nearest device pixel — outward it costs the leading partially
/// covered column, and inward it takes the whole picture. What a shrink is authored through must not
/// decide whether it survives.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ShrinkingTransformKeepsItsFootprintTests
{
    /// <summary>
    /// Skia publishes coverage in 1/255 steps, so a per-pixel alpha lands within one quantum of the
    /// analytic value; over a whole column that accumulates.
    /// </summary>
    private const double CoverageTolerance = 0.2;

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void AShrinkingTransform_PaintsTheSameFootprintAsTheShapeItProduces(float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // The same 0.08 x 60 rectangle at (88, 42), authored twice: once as the shape's own size,
            // once as a 1000x shrink of an 80 x 60 shape.
            double authored = MeasureInk(Rectangle(0.08f, 60f, scaleX: null), outputScale);
            double shrunk = MeasureInk(Rectangle(80f, 60f, scaleX: 0.1f), outputScale);

            Assert.That(
                shrunk,
                Is.GreaterThan(0),
                "a finite shrink with a nonzero determinant keeps a non-empty device footprint, so it "
                + "must not render as an empty frame.");
            Assert.That(
                shrunk,
                Is.EqualTo(authored).Within(CoverageTolerance),
                "the two scenes describe one rectangle, so the route the shrink is authored through "
                + "must not change how much of it is painted.");
        });
    }

    [Test]
    public void AShrinkingTransform_KeepsItsFootprintAcrossOutputScales()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // A 0.4 x 0.3 device-pixel dot: below one pixel on both axes at every scale sampled here,
            // so nothing but a rounding boundary could make it appear, vanish, and reappear.
            foreach (float outputScale in new[] { 0.5f, 1f, 1.5f, 2f })
            {
                double ink = MeasureInk(Rectangle(80f, 60f, scaleX: 0.5f, scaleY: 0.5f), outputScale);
                double analytic = 0.4 * 0.3 * outputScale * outputScale;
                Assert.That(
                    ink,
                    Is.EqualTo(analytic).Within(CoverageTolerance),
                    $"the dot covers {analytic:F4} device pixels at output scale {outputScale}, and "
                    + "coverage has to follow the input rather than the device grid it lands on.");
            }
        });
    }

    private static RectShape Rectangle(float width, float height, float? scaleX, float? scaleY = null)
    {
        var rectangle = new RectShape();
        rectangle.Width.CurrentValue = width;
        rectangle.Height.CurrentValue = height;
        rectangle.Fill.CurrentValue = new SolidColorBrush(Colors.OrangeRed);
        rectangle.AlignmentX.CurrentValue = AlignmentX.Left;
        rectangle.AlignmentY.CurrentValue = AlignmentY.Top;
        rectangle.TransformOrigin.CurrentValue = RelativePoint.TopLeft;

        var group = new TransformGroup();
        var translate = new TranslateTransform();
        translate.X.CurrentValue = 88;
        translate.Y.CurrentValue = 42;
        // A TransformGroup applies its last child first, so the shrink runs in the shape's own space.
        group.Children.Add(translate);
        if (scaleX is { } percentX)
        {
            var scale = new ScaleTransform();
            scale.ScaleX.CurrentValue = percentX;
            scale.ScaleY.CurrentValue = scaleY ?? 100f;
            group.Children.Add(scale);
        }

        rectangle.Transform.CurrentValue = group;
        return rectangle;
    }

    private static double MeasureInk(Drawable drawable, float outputScale)
    {
        var scene = new Scene(640, 360, "shrink") { Uri = new Uri("file:///shrink/scene") };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 0,
            IsEnabled = true,
            Uri = new Uri("file:///shrink/element"),
        };
        element.AddObject(drawable);
        scene.Children.Add(element);

        using var renderer = new SceneRenderer(scene, RenderIntent.Preview, outputScale, false, outputScale * 2f)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.Zero));
        using Bitmap bitmap = renderer.Snapshot();

        double ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y)[..(bitmap.Width * 4)];
            for (int x = 3; x < row.Length; x += 4)
                ink += (float)BitConverter.UInt16BitsToHalf(row[x]);
        }

        return ink;
    }
}
