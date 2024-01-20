using Beutl.Collections;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Graphics.UnitTests;

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
        var shape = new RectShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Width = 100,
            Height = 100,
            Fill = Brushes.White
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawRectangleWithPen()
    {
        var shape = new RectShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Width = 100,
            Height = 100,

            Fill = Brushes.Gray,
            Pen = new Pen()
            {
                Brush = Brushes.White,
                Thickness = 10,
                StrokeCap = StrokeCap.Round,
                DashArray = [2],
            }
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);

        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawEllipse()
    {
        var shape = new EllipseShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Width = 100,
            Height = 100,
            Fill = Brushes.White
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawRoundedRect()
    {
        var shape = new RoundedRectShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Width = 100,
            Height = 100,
            CornerRadius = new CornerRadius(25),

            Fill = Brushes.White
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    [TestCase(StrokeAlignment.Center)]
    [TestCase(StrokeAlignment.Inside)]
    [TestCase(StrokeAlignment.Outside)]
    public void DrawRoundedRectWithStroke(StrokeAlignment alignment)
    {
        var shape = new RoundedRectShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Width = 100,
            Height = 100,
            CornerRadius = new CornerRadius(25),

            Fill = Brushes.Gray,
            Pen = new Pen()
            {
                Brush = Brushes.White,
                Thickness = 10,
                StrokeCap = StrokeCap.Round,
                StrokeAlignment = alignment,
            }
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"{alignment}.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawGeometry()
    {
        var geometry = new PathGeometry();
        var center = new Point(100, 100);
        float radius = 0.45f * Math.Min(200, 200);

        geometry.MoveTo(new Point(100, 100 - radius));

        for (int i = 1; i < 5; i++)
        {
            float angle = i * 4 * MathF.PI / 5;
            geometry.LineTo(center + new Point(radius * MathF.Sin(angle), -radius * MathF.Cos(angle)));
        }
        geometry.Close();

        var shape = new GeometryShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Data = geometry,
            Fill = Brushes.White
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

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
        var geometry = new PathGeometry()
        {
            FillType = fillType
        };
        var center = new Point(100, 100);
        float radius = 0.45f * Math.Min(200, 200);

        geometry.MoveTo(new Point(100, 100 - radius));

        for (int i = 1; i < 5; i++)
        {
            float angle = i * 4 * MathF.PI / 5;
            geometry.LineTo(center + new Point(radius * MathF.Sin(angle), -radius * MathF.Cos(angle)));
        }
        geometry.Close();

        var shape = new GeometryShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,

            Data = geometry,

            Fill = Brushes.Gray,
            Pen = new Pen()
            {
                Brush = Brushes.White,
                Thickness = 10,
                StrokeCap = StrokeCap.Round,
                StrokeAlignment = alignment,
            }
        };

        using var canvas = new ImmediateCanvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Render(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"{alignment}_{fillType}.png"), EncodedImageFormat.Png));
    }
}
