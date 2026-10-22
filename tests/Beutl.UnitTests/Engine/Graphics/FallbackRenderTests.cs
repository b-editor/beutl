using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics;

// A transform, drawable or path segment whose type did not load, for example from a plugin that is not installed,
// leaves the frame as it would be without it.
public class FallbackRenderTests
{
    private const int Size = 32;

    [Test]
    public void AFallbackTransform_LeavesItsTargetInPlace()
        => AssertSameFrame(Square(null), Square(new FallbackTransform()));

    [Test]
    public void AFallbackDrawable_DrawsNothing()
    {
        var plain = new DrawableGroup();
        plain.Children.Add(Square(null));
        var withFallback = new DrawableGroup();
        withFallback.Children.Add(Square(null));
        withFallback.Children.Add(new FallbackDrawable());

        AssertSameFrame(plain, withFallback);
    }

    [Test]
    public void AFallbackPathSegment_AddsNothingToThePath()
        => AssertSameFrame(Triangle(withFallback: false), Triangle(withFallback: true));

    private static RectShape Square(Transform? transform)
    {
        var shape = new RectShape();
        shape.Width.Value = Size / 2;
        shape.Height.Value = Size / 2;
        shape.Fill.Value = Brushes.White;
        shape.Transform.Value = transform;
        return shape;
    }

    private static GeometryShape Triangle(bool withFallback)
    {
        var figure = new PathFigure();
        figure.StartPoint.Value = new Point(4, 4);
        figure.IsClosed.Value = true;
        figure.Segments.Add(new LineSegment(28, 4));
        if (withFallback)
            figure.Segments.Add(new FallbackPathSegment());
        figure.Segments.Add(new LineSegment(16, 28));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var shape = new GeometryShape();
        shape.Data.Value = geometry;
        shape.Fill.Value = Brushes.White;
        return shape;
    }

    private static void AssertSameFrame(Drawable expected, Drawable actual)
    {
        using Bitmap plain = Render(expected);
        using Bitmap withFallback = Render(actual);

        Assert.Multiple(() =>
        {
            Assert.That(ImageMetrics.ReadPixel(plain, Size / 2, Size / 2).Alpha, Is.GreaterThan(0.95f));
            Assert.That(withFallback.GetPixelSpan().ToArray(), Is.EqualTo(plain.GetPixelSpan().ToArray()));
        });
    }

    private static Bitmap Render(Drawable drawable)
        => RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = RendererTestSupport.CreateCpuRenderer(Size, Size);
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(Size, Size), drawable));
            return renderer.Snapshot();
        });
}
