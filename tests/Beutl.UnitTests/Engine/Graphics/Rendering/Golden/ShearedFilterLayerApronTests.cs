using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class ShearedFilterLayerApronTests
{
    private static readonly Rect s_content = new(12, 7, 40, 24);

    /// <summary>The device transform observed replaying a blur under an 80 degree skew at output scale 2.</summary>
    private static readonly Matrix s_replayedSkew = new(2f, 0f, 1.1342561f, 0.2f, 0f, 0f);

    /// <summary>Skia publishes coverage in 1/255 steps, which accumulates over a whole bar.</summary>
    private const double InkTolerance = 0.02;

    public static IEnumerable<TestCaseData> ShearedTransforms()
    {
        yield return new TestCaseData(Matrix.CreateSkew(MathF.PI / 4, 0f)).SetName("skew x by 45 degrees");
        yield return new TestCaseData(Matrix.CreateSkew(0f, -1.4f)).SetName("steep negative skew in y");
        yield return new TestCaseData(s_replayedSkew).SetName("replayed 80 degree skew at output scale 2");
        yield return new TestCaseData(
                Matrix.CreateSkew(1.3f, 0f).Append(Matrix.CreateScale(3f, 0.25f)))
            .SetName("skew under an anisotropic scale");
        yield return new TestCaseData(
                Matrix.CreateSkew(0.9f, 0.4f).Append(Matrix.CreateRotation(0.7f)))
            .SetName("skew under a rotation");
    }

    public static IEnumerable<TestCaseData> UnshearedTransforms()
    {
        yield return new TestCaseData(Matrix.Identity).SetName("identity");
        yield return new TestCaseData(Matrix.CreateScale(2f, 2f)).SetName("uniform scale");
        yield return new TestCaseData(Matrix.CreateScale(10f, 0.1f)).SetName("anisotropic scale");
        yield return new TestCaseData(Matrix.CreateScale(0.333f, 0.333f)).SetName("fractional scale");
        yield return new TestCaseData(Matrix.CreateRotation(0.7f)).SetName("rotation");
        yield return new TestCaseData(Matrix.CreateRotation(0.7f).Append(Matrix.CreateScale(3f, 3f)))
            .SetName("rotation under a uniform scale");
        yield return new TestCaseData(Matrix.CreateTranslation(31f, -12f).Prepend(Matrix.CreateScale(1.5f, 4f)))
            .SetName("translated scale");
    }

    [TestCaseSource(nameof(ShearedTransforms))]
    [TestCaseSource(nameof(UnshearedTransforms))]
    public void TheApron_HoldsOneDevicePixelPerpendicularToEveryEdge(Matrix transform)
    {
        Rect inflated = ImmediateCanvas.InflateByOneDevicePixel(s_content, transform);

        Assert.Multiple(() =>
        {
            Assert.That(
                EdgeMargin(s_content.TopLeft, s_content.BottomLeft, new Point(inflated.X, s_content.Y), transform),
                Is.EqualTo(1d).Within(1e-4),
                "left edge");
            Assert.That(
                EdgeMargin(s_content.TopRight, s_content.BottomRight, new Point(inflated.Right, s_content.Y), transform),
                Is.EqualTo(1d).Within(1e-4),
                "right edge");
            Assert.That(
                EdgeMargin(s_content.TopLeft, s_content.TopRight, new Point(s_content.X, inflated.Y), transform),
                Is.EqualTo(1d).Within(1e-4),
                "top edge");
            Assert.That(
                EdgeMargin(s_content.BottomLeft, s_content.BottomRight, new Point(s_content.X, inflated.Bottom), transform),
                Is.EqualTo(1d).Within(1e-4),
                "bottom edge");
        });
    }

    [TestCaseSource(nameof(UnshearedTransforms))]
    public void AnOrthogonalBasis_KeepsTheReciprocalApronExactly(Matrix transform)
    {
        float devicePerX = MathF.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        float devicePerY = MathF.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));

        Assert.That(
            ImmediateCanvas.InflateByOneDevicePixel(s_content, transform),
            Is.EqualTo(s_content.Inflate(new Thickness(1f / devicePerX, 1f / devicePerY))));
    }

    [TestCase(1f, 1f, 1f, 1f, TestName = "collapsed onto a line")]
    [TestCase(0f, 0f, 0f, 0f, TestName = "collapsed onto a point")]
    [TestCase(1f, float.NaN, 0f, 1f, TestName = "not a number")]
    [TestCase(1f, 0f, float.PositiveInfinity, 1f, TestName = "not finite")]
    public void ADegenerateBasis_LeavesTheBoundsAlone(float m11, float m12, float m21, float m22)
    {
        Assert.That(
            ImmediateCanvas.InflateByOneDevicePixel(s_content, new Matrix(m11, m12, m21, m22, 0f, 0f)),
            Is.EqualTo(s_content));
    }

    [TestCase(0f, TestName = "unsheared control")]
    [TestCase(80f, TestName = "80 degree skew")]
    [Category("GpuPassFusionGpu")]
    public void ANegligibleBlurOnShearedContent_KeepsTheInkItHasWithoutTheBlur(float skewX)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            double unfiltered = 0;
            double filtered = 0;
            for (int phase = 0; phase < 20; phase++)
            {
                float offsetY = 150f + (phase * 0.025f);
                unfiltered += MeasureInk(null, skewX, offsetY);
                filtered += MeasureInk(NegligibleBlur(), skewX, offsetY);
            }

            Assert.That(unfiltered, Is.GreaterThan(0), "the bar has to render, or the comparison proves nothing.");
            Assert.That(
                filtered / unfiltered,
                Is.EqualTo(1d).Within(InkTolerance),
                $"a blur of sigma 0.01 at skew {skewX} painted {filtered:F2} of ink where the same content "
                + $"without it paints {unfiltered:F2}; a filter that moves no pixel must not cost the "
                + "content the coverage its layer failed to make room for.");
        });
    }

    /// <summary>
    /// The perpendicular distance between the transformed edge through <paramref name="inside"/> and the
    /// parallel transformed edge through <paramref name="outside"/>, in device pixels.
    /// </summary>
    private static double EdgeMargin(Point inside, Point along, Point outside, Matrix transform)
    {
        Point a = inside * transform;
        Point b = along * transform;
        Point c = outside * transform;
        var direction = new Vector(b.X - a.X, b.Y - a.Y);
        var offset = new Vector(c.X - a.X, c.Y - a.Y);
        return Math.Abs((offset.X * direction.Y) - (offset.Y * direction.X)) / direction.Length;
    }

    private static FilterEffect NegligibleBlur()
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(0.01f, 0.01f);
        return blur;
    }

    /// <summary>
    /// Renders a 100 x 6 bar squeezed to 0.6 logical units tall, skewed in x, and sums the alpha it
    /// leaves on the frame.
    /// </summary>
    private static double MeasureInk(FilterEffect? effect, float skewX, float offsetY)
    {
        var rectangle = new RectShape();
        rectangle.Width.CurrentValue = 100f;
        rectangle.Height.CurrentValue = 6f;
        rectangle.Fill.CurrentValue = new SolidColorBrush(Colors.White);
        rectangle.AlignmentX.CurrentValue = AlignmentX.Left;
        rectangle.AlignmentY.CurrentValue = AlignmentY.Top;
        rectangle.TransformOrigin.CurrentValue = RelativePoint.TopLeft;
        if (effect is not null)
            rectangle.FilterEffect.CurrentValue = effect;

        var group = new TransformGroup();
        var translate = new TranslateTransform();
        translate.X.CurrentValue = 64;
        translate.Y.CurrentValue = offsetY;
        // A TransformGroup applies its last child first, so the squeeze runs in the shape's own space.
        group.Children.Add(translate);
        var skew = new SkewTransform();
        skew.SkewX.CurrentValue = skewX;
        group.Children.Add(skew);
        var scale = new ScaleTransform();
        scale.ScaleX.CurrentValue = 100f;
        scale.ScaleY.CurrentValue = 10f;
        group.Children.Add(scale);
        rectangle.Transform.CurrentValue = group;

        var scene = new Scene(640, 360, "sheared-apron") { Uri = new Uri("file:///sheared-apron/scene") };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 0,
            IsEnabled = true,
            Uri = new Uri("file:///sheared-apron/element"),
        };
        element.AddObject(rectangle);
        scene.Children.Add(element);

        using var renderer = new SceneRenderer(scene, RenderIntent.Preview, 2f, false, 4f)
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
