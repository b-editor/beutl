using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Legacy;

namespace Beutl.UnitTests.Engine;

public class ShapeTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void DrawRectangle()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 100;
        shape.Fill.CurrentValue = Brushes.White;
        var resource = shape.ToResource(RenderContext.Default);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);
        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawRectangleWithPen()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 100;
        shape.Fill.CurrentValue = Brushes.Gray;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.White;
        pen.Thickness.CurrentValue = 10;
        pen.StrokeCap.CurrentValue = StrokeCap.Round;
        pen.DashArray.CurrentValue = [2];
        shape.Pen.CurrentValue = pen;

        var resource = shape.ToResource(RenderContext.Default);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);

        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawEllipse()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 100;
        shape.Fill.CurrentValue = Brushes.White;
        var resource = shape.ToResource(RenderContext.Default);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);
        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawRoundedRect()
    {
        var shape = new RoundedRectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 100;
        shape.CornerRadius.CurrentValue = new CornerRadius(25);
        shape.Fill.CurrentValue = Brushes.White;
        var resource = shape.ToResource(RenderContext.Default);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);
        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    [TestCase(StrokeAlignment.Center)]
    [TestCase(StrokeAlignment.Inside)]
    [TestCase(StrokeAlignment.Outside)]
    public void DrawRoundedRectWithStroke(StrokeAlignment alignment)
    {
        var shape = new RoundedRectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 100;
        shape.CornerRadius.CurrentValue = new CornerRadius(25);
        shape.Fill.CurrentValue = Brushes.Gray;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.White;
        pen.Thickness.CurrentValue = 10;
        pen.StrokeCap.CurrentValue = StrokeCap.Round;
        pen.StrokeAlignment.CurrentValue = alignment;
        shape.Pen.CurrentValue = pen;
        var resource = shape.ToResource(RenderContext.Default);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);
        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"{alignment}.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawGeometry()
    {
        var figure = new PathFigure();
        var geometry = new PathGeometry { Figures = { figure } };
        var center = new Point(100, 100);
        float radius = 0.45f * Math.Min(200, 200);

        figure.StartPoint.CurrentValue = new Point(100, 100 - radius);

        for (int i = 1; i < 5; i++)
        {
            float angle = i * 4 * MathF.PI / 5;
            figure.Segments.Add(
                new LineSegment(center + new Point(radius * MathF.Sin(angle), -radius * MathF.Cos(angle))));
        }

        figure.IsClosed.CurrentValue = true;

        var shape = new GeometryShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Data.CurrentValue = geometry;
        shape.Fill.CurrentValue = Brushes.White;

        var resource = shape.ToResource(RenderContext.Default);
        shape.Transform.CurrentValue = new TranslateTransform(-resource.Data!.Bounds.Position);
        bool updateOnly = false;
        resource.Update(shape, RenderContext.Default, ref updateOnly);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);
        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    [TestCase(StrokeAlignment.Center, PathFillType.Winding)]
    [TestCase(StrokeAlignment.Inside, PathFillType.Winding)]
    [TestCase(StrokeAlignment.Outside, PathFillType.Winding)]
    [TestCase(StrokeAlignment.Center, PathFillType.EvenOdd)]
    [TestCase(StrokeAlignment.Inside, PathFillType.EvenOdd)]
    [TestCase(StrokeAlignment.Outside, PathFillType.EvenOdd)]
    public void DrawGeometryWithPen(StrokeAlignment alignment, PathFillType fillType)
    {
        var figure = new PathFigure();
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.FillType.CurrentValue = fillType;
        var center = new Point(100, 100);
        float radius = 0.45f * Math.Min(200, 200);

        figure.StartPoint.CurrentValue = new Point(100, 100 - radius);

        for (int i = 1; i < 5; i++)
        {
            float angle = i * 4 * MathF.PI / 5;
            figure.Segments.Add(
                new LineSegment(center + new Point(radius * MathF.Sin(angle), -radius * MathF.Cos(angle))));
        }

        figure.IsClosed.CurrentValue = true;

        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.White;
        pen.Thickness.CurrentValue = 10;
        pen.StrokeCap.CurrentValue = StrokeCap.Round;
        pen.StrokeAlignment.CurrentValue = alignment;

        var shape = new GeometryShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Data.CurrentValue = geometry;
        shape.Fill.CurrentValue = Brushes.Gray;
        shape.Pen.CurrentValue = pen;

        var resource = shape.ToResource(RenderContext.Default);
        shape.Transform.CurrentValue = new TranslateTransform(-resource.Data!.Bounds.Position);
        bool updateOnly = false;
        resource.Update(shape, RenderContext.Default, ref updateOnly);

        using var renderTarget = RenderTarget.Create(250, 250)!;
        using var canvas = new ImmediateCanvas(renderTarget);

        canvas.Clear(Colors.Black);
        canvas.DrawDrawable(resource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"{alignment}_{fillType}.png"), EncodedImageFormat.Png));
    }
}
