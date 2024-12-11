using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class GeometryRenderNodeTest
{
    [Test]
    public void Equals_ShouldReturnTrue_WhenAllPropertiesMatch()
    {
        var geometry = new EllipseGeometry { Width = 100, Height = 100 };
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };

        var node = new GeometryRenderNode(geometry, fill, pen);

        Assert.That(node.Equals(geometry, fill, pen), Is.True);
    }

    [Test]
    public void Equals_ShouldReturnFalse_WhenPropertiesDoNotMatch()
    {
        var geometry1 = new EllipseGeometry { Width = 100, Height = 100 };
        var geometry2 = new EllipseGeometry { Width = 100, Height = 100 };
        IBrush fill1 = new SolidColorBrush(Colors.Red);
        IBrush fill2 = new SolidColorBrush(Colors.Blue);
        IPen pen1 = new Pen { Brush = Brushes.Black, Thickness = 1 };
        IPen pen2 = new Pen { Brush = Brushes.Black, Thickness = 2 };

        var node = new GeometryRenderNode(geometry1, fill1, pen1);

        Assert.That(node.Equals(geometry2, fill1, pen1), Is.False);
        Assert.That(node.Equals(geometry1, fill2, pen1), Is.False);
        Assert.That(node.Equals(geometry1, fill1, pen2), Is.False);
    }

    [Test]
    public void Process_ShouldReturnCorrectRenderNodeOperation()
    {
        var geometry = new EllipseGeometry { Width = 100, Height = 100 };
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometry, fill, pen);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Null);
        Assert.That(operations.Length, Is.EqualTo(1));
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideGeometry()
    {
        var geometry = new EllipseGeometry { Width = 100, Height = 100 };
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometry, fill, pen);
        var operations = node.Process(context);
        var point = new Point(50, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideGeometry()
    {
        var geometry = new EllipseGeometry { Width = 100, Height = 100 };
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometry, fill, pen);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideGeometryStroke()
    {
        var geometry = new EllipseGeometry { Width = 100, Height = 100 };
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 50 };
        var context = new RenderNodeContext([]);

        var node = new GeometryRenderNode(geometry, null, pen);
        var operations = node.Process(context);
        var point = new Point(0, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }
}
