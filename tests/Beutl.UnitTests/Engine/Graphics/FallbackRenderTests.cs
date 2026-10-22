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

    // Without a start point, an open figure starts where its first segment ends and a closed one where its last ends.
    [TestCase(false)]
    [TestCase(true)]
    public void AFallbackPathSegment_WhereAFigureTakesItsStart_LeavesTheStartToTheOtherSegments(bool closed)
        => AssertSameFrame(PathShape(Figure(closed, withFallback: false)), PathShape(Figure(closed, withFallback: true)));

    [Test]
    public void AFallbackPathSegment_DoesNotJoinItsFigureToTheOneBefore()
        => AssertSameFrame(
            PathShape(Corner(), Figure(closed: false, withFallback: false)),
            PathShape(Corner(), Figure(closed: false, withFallback: true)));

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

    // A triangle without a start point, with the fallback where the figure takes its start from.
    private static PathFigure Figure(bool closed, bool withFallback)
    {
        var figure = new PathFigure();
        figure.IsClosed.Value = closed;
        figure.Segments.Add(new LineSegment(4, 4));
        figure.Segments.Add(new LineSegment(28, 4));
        figure.Segments.Add(new LineSegment(16, 28));
        if (withFallback)
            figure.Segments.Insert(closed ? figure.Segments.Count : 0, new FallbackPathSegment());
        return figure;
    }

    // An open corner whose end the next figure would continue from if it did not start on its own.
    private static PathFigure Corner()
    {
        var figure = new PathFigure();
        figure.Segments.Add(new LineSegment(1, 1));
        figure.Segments.Add(new LineSegment(3, 1));
        figure.Segments.Add(new LineSegment(1, 6));
        return figure;
    }

    private static GeometryShape PathShape(params PathFigure[] figures)
    {
        var geometry = new PathGeometry();
        foreach (PathFigure figure in figures)
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
