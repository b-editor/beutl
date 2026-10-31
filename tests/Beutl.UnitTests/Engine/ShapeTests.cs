using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine;

// Each shape is centred in a transparent 250x250 frame and drawn by the renderer; the tests read a few pixels
// inside and outside its fill and its stroke. The 100x100 shapes cover 75 to 175 on both axes.
[NonParallelizable]
public class ShapeTests
{
    private const int FrameSize = 250;

    public enum Paint
    {
        None,
        Gray,
        White,
        Mixed,
    }

    [Test]
    public void DrawRectangle()
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 100;
        shape.Fill.Value = Brushes.White;

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (125, 125, Paint.White),
            (77, 77, Paint.White),
            (172, 172, Paint.White),
            (72, 125, Paint.None),
            (125, 178, Paint.None),
            (5, 5, Paint.None));
    }

    [Test]
    public void DrawRectangleWithPen()
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 100;
        shape.Fill.Value = Brushes.Gray;
        var pen = new Pen();
        pen.Brush.Value = Brushes.White;
        pen.Thickness.Value = 10;
        pen.StrokeCap.Value = StrokeCap.Round;
        pen.DashArray.Value = [2];
        shape.Pen.Value = pen;

        using Bitmap bitmap = Render(shape);

        // The dashes are 20 px long with 20 px gaps, so the column just inside the left edge shows both the
        // stroke and the fill beneath it.
        Paint[] leftEdge = [.. Enumerable.Range(85, 80).Select(y => PaintAt(bitmap, 76, y))];
        AssertPaint(bitmap, (125, 125, Paint.Gray), (5, 5, Paint.None), (60, 125, Paint.None));
        Assert.That(leftEdge, Has.Some.EqualTo(Paint.White));
        Assert.That(leftEdge, Has.Some.EqualTo(Paint.Gray));
    }

    [Test]
    public void DrawEllipse()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 100;
        shape.Fill.Value = Brushes.White;

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (125, 125, Paint.White),
            (125, 78, Paint.White),
            (78, 125, Paint.White),
            (80, 80, Paint.None),
            (170, 170, Paint.None),
            (5, 5, Paint.None));
    }

    [Test]
    public void DrawRoundedRect()
    {
        var shape = new RoundedRectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 100;
        shape.CornerRadius.Value = new CornerRadius(25);
        shape.Fill.Value = Brushes.White;

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (125, 125, Paint.White),
            (125, 77, Paint.White),
            (77, 125, Paint.White),
            (78, 78, Paint.None),
            (171, 171, Paint.None),
            (5, 5, Paint.None));
    }

    // Along the middle row the 10 px stroke straddles, fills or surrounds the left edge at x = 75.
    [TestCase(StrokeAlignment.Center, Paint.None, Paint.White, Paint.White, Paint.Gray)]
    [TestCase(StrokeAlignment.Inside, Paint.None, Paint.None, Paint.White, Paint.White)]
    [TestCase(StrokeAlignment.Outside, Paint.White, Paint.White, Paint.Gray, Paint.Gray)]
    public void DrawRoundedRectWithStroke(StrokeAlignment alignment, Paint at67, Paint at72, Paint at77, Paint at82)
    {
        var shape = new RoundedRectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 100;
        shape.CornerRadius.Value = new CornerRadius(25);
        shape.Fill.Value = Brushes.Gray;
        var pen = new Pen();
        pen.Brush.Value = Brushes.White;
        pen.Thickness.Value = 10;
        pen.StrokeCap.Value = StrokeCap.Round;
        pen.StrokeAlignment.Value = alignment;
        shape.Pen.Value = pen;

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (61, 125, Paint.None),
            (67, 125, at67),
            (72, 125, at72),
            (77, 125, at77),
            (82, 125, at82),
            (125, 125, Paint.Gray),
            (5, 5, Paint.None));
    }

    // Drawn below the fill, the part of the stroke inside the left edge at x = 75 is covered by the fill.
    [TestCase(StrokeAlignment.Center, Paint.White, Paint.Gray)]
    [TestCase(StrokeAlignment.Inside, Paint.None, Paint.Gray)]
    public void DrawRoundedRectWithStrokeBelowFill(StrokeAlignment alignment, Paint at72, Paint at77)
    {
        var shape = new RoundedRectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 100;
        shape.Height.Value = 100;
        shape.CornerRadius.Value = new CornerRadius(25);
        shape.Fill.Value = Brushes.Gray;
        var pen = new Pen();
        pen.Brush.Value = Brushes.White;
        pen.Thickness.Value = 10;
        pen.StrokeAlignment.Value = alignment;
        pen.StrokeOrder.Value = StrokeOrder.BelowFill;
        shape.Pen.Value = pen;

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (67, 125, Paint.None),
            (72, 125, at72),
            (77, 125, at77),
            (125, 125, Paint.Gray));
    }

    // A thick stroke reaches over the neighbouring glyph or run; the colour tag starts a second run in the same gray.
    // Placed below the fill, the stroke stays below every fill in the block, so every pixel a fill covers fully keeps
    // the fill's colour.
    [TestCase(false, WritingMode.HorizontalTb, "Beu<#808080>tl</#>")]
    [TestCase(true, WritingMode.HorizontalTb, "Beutl")]
    [TestCase(true, WritingMode.VerticalRl, "あいう")]
    public void TextStrokeBelowFillStaysBelowEveryFill(bool split, WritingMode writingMode, string text)
    {
        using Bitmap fillOnly = Render(CreateTextBelowFill(split, writingMode, text, Brushes.Transparent));
        using Bitmap stroked = Render(CreateTextBelowFill(split, writingMode, text, Brushes.White));

        int filled = 0;
        int covered = 0;
        for (int y = 0; y < FrameSize; y++)
        {
            for (int x = 0; x < FrameSize; x++)
            {
                if (ImageMetrics.AlphaAt(fillOnly, x, y) < 0.99f)
                    continue;

                filled++;
                if (PaintAt(stroked, x, y) != Paint.Gray)
                    covered++;
            }
        }

        Assert.That(filled, Is.GreaterThan(500), "the fixture must fill glyphs");
        Assert.That(covered, Is.Zero);
    }

    private static TextBlock CreateTextBelowFill(bool split, WritingMode writingMode, string text, Brush penBrush)
    {
        var pen = new Pen();
        pen.Brush.Value = penBrush;
        pen.Thickness.Value = 24;
        pen.StrokeOrder.Value = StrokeOrder.BelowFill;

        var block = new TextBlock();
        block.FontFamily.Value = TypefaceProvider.Typeface().FontFamily;
        block.Size.Value = 48;
        block.Text.Value = text;
        block.SplitByCharacters.Value = split;
        block.WritingMode.Value = writingMode;
        block.Fill.Value = Brushes.Gray;
        block.Pen.Value = pen;
        return block;
    }

    [Test]
    public void DrawGeometry()
    {
        GeometryShape shape = CreateStar(PathFillType.Winding);
        shape.Fill.Value = Brushes.White;
        MoveBoundsToOrigin(shape);

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (125, StarCenterY, Paint.White),
            (125, StarSpikeY, Paint.White),
            (125, StarTipY, Paint.None),
            (125, StarNotchY, Paint.None),
            (5, 5, Paint.None));
    }

    // 24 px below the top tip the spike's edges are 7 px away, past a centred stroke but within an inside one.
    // The star's centre is the inner pentagon an even-odd fill leaves empty.
    [TestCase(StrokeAlignment.Center, PathFillType.Winding, Paint.Gray, Paint.Gray, Paint.White)]
    [TestCase(StrokeAlignment.Inside, PathFillType.Winding, Paint.Gray, Paint.White, Paint.None)]
    [TestCase(StrokeAlignment.Outside, PathFillType.Winding, Paint.Gray, Paint.Gray, Paint.White)]
    [TestCase(StrokeAlignment.Center, PathFillType.EvenOdd, Paint.None, Paint.Gray, Paint.White)]
    [TestCase(StrokeAlignment.Inside, PathFillType.EvenOdd, Paint.None, Paint.White, Paint.None)]
    [TestCase(StrokeAlignment.Outside, PathFillType.EvenOdd, Paint.None, Paint.Gray, Paint.White)]
    public void DrawGeometryWithPen(
        StrokeAlignment alignment,
        PathFillType fillType,
        Paint atCenter,
        Paint inSpike,
        Paint aboveTip)
    {
        var pen = new Pen();
        pen.Brush.Value = Brushes.White;
        pen.Thickness.Value = 10;
        pen.StrokeCap.Value = StrokeCap.Round;
        pen.StrokeAlignment.Value = alignment;

        GeometryShape shape = CreateStar(fillType);
        shape.Fill.Value = Brushes.Gray;
        shape.Pen.Value = pen;
        MoveBoundsToOrigin(shape);

        using Bitmap bitmap = Render(shape);

        AssertPaint(
            bitmap,
            (125, StarCenterY, atCenter),
            (125, StarSpikeY, inSpike),
            (125, StarTipY, aboveTip),
            (125, StarNotchY, Paint.None),
            (5, 5, Paint.None));
    }

    // A pentagram of radius 90 about (100, 100). Centring its bounds moves (100, 100) to (125, 133.6) in the
    // frame, its top tip to (125, 43.6) and the inner pentagon's lowest corner to (125, 168).
    private const int StarCenterY = 133;
    private const int StarSpikeY = 67;
    private const int StarTipY = 38;
    private const int StarNotchY = 183;

    private static GeometryShape CreateStar(PathFillType fillType)
    {
        var figure = new PathFigure();
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.FillType.Value = fillType;
        var center = new Point(100, 100);
        float radius = 0.45f * Math.Min(200, 200);

        figure.StartPoint.Value = new Point(100, 100 - radius);

        for (int i = 1; i < 5; i++)
        {
            float angle = i * 4 * MathF.PI / 5;
            figure.Segments.Add(
                new LineSegment(center + new Point(radius * MathF.Sin(angle), -radius * MathF.Cos(angle))));
        }

        figure.IsClosed.Value = true;

        var shape = new GeometryShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Data.Value = geometry;
        return shape;
    }

    // Moves the geometry's bounds to the origin, so the alignment centres the star itself.
    private static void MoveBoundsToOrigin(GeometryShape shape)
    {
        using var resource = shape.ToResource(CompositionContext.Default);
        shape.Transform.Value = new TranslateTransform(-resource.Data!.Bounds.Position);
    }

    private static Bitmap Render(Drawable drawable)
        => RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = RendererTestSupport.CreateCpuRenderer(FrameSize, FrameSize);
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(FrameSize, FrameSize), drawable));
            return renderer.Snapshot();
        });

    private static Paint PaintAt(Bitmap bitmap, int x, int y)
    {
        Rgba pixel = ImageMetrics.ReadPixel(bitmap, x, y);
        if (pixel.Alpha < 0.05f)
            return Paint.None;
        if (pixel.Alpha < 0.95f)
            return Paint.Mixed;

        // Gray is 0.22 in the linear target.
        return pixel.Red switch
        {
            > 0.95f => Paint.White,
            > 0.15f and < 0.3f => Paint.Gray,
            _ => Paint.Mixed,
        };
    }

    private static void AssertPaint(Bitmap bitmap, params (int X, int Y, Paint Expected)[] samples)
    {
        using (Assert.EnterMultipleScope())
        {
            foreach ((int x, int y, Paint expected) in samples)
                Assert.That(PaintAt(bitmap, x, y), Is.EqualTo(expected), $"pixel ({x}, {y})");
        }
    }
}
