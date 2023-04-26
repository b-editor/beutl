using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Collections;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;

using NUnit.Framework;

namespace Beutl.Graphics.UnitTests;

public class ShapeTests
{
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
            Foreground = Brushes.White
        };

        using var canvas = new Canvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Draw(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
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

            Foreground = Brushes.Gray,
            Pen = new Pen()
            {
                Brush = Brushes.White,
                Thickness = 10,
                StrokeCap = StrokeCap.Round,
                DashArray = new CoreList<float>()
                {
                    2
                },
            }
        };

        using var canvas = new Canvas(250, 250);

        canvas.Clear(Colors.Black);

        shape.Draw(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
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
            Foreground = Brushes.White
        };

        using var canvas = new Canvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Draw(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
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

            Foreground = Brushes.White
        };

        using var canvas = new Canvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Draw(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawGeometry()
    {
        var geometry = new PathGeometry();
        var center = new Point(50, 50);
        float radius = 0.45f * Math.Min(100, 100);

        geometry.MoveTo(new Point(50, 50 - radius));

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
            Foreground = Brushes.White
        };

        using var canvas = new Canvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Draw(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void DrawGeometryWithPen()
    {
        var geometry = new PathGeometry();
        var center = new Point(50, 50);
        float radius = 0.45f * Math.Min(100, 100);

        geometry.MoveTo(new Point(50, 50 - radius));

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

            Foreground = Brushes.Gray,
            Pen = new Pen()
            {
                Brush = Brushes.White,
                Thickness = 10,
                StrokeCap = StrokeCap.Round,
            }
        };

        using var canvas = new Canvas(250, 250);

        canvas.Clear(Colors.Black);
        shape.Draw(canvas);

        using Bitmap<Bgra8888> bmp = canvas.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }
}
