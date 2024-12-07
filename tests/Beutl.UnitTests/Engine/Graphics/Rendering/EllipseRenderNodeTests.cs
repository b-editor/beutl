using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Graphics;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class EllipseRenderNodeTest
{
    [Test]
    public void Equals_ShouldReturnTrue_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };

        var node = new EllipseRenderNode(rect, fill, pen);

        Assert.That(node.Equals(rect, fill, pen), Is.True);
    }

    [Test]
    public void Equals_ShouldReturnFalse_WhenPropertiesDoNotMatch()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(0, 0, 200, 200);
        IBrush fill1 = new SolidColorBrush(Colors.Red);
        IBrush fill2 = new SolidColorBrush(Colors.Blue);
        IPen pen1 = new Pen { Brush = Brushes.Black, Thickness = 1 };
        IPen pen2 = new Pen { Brush = Brushes.Black, Thickness = 2 };

        var node = new EllipseRenderNode(rect1, fill1, pen1);

        Assert.That(node.Equals(rect2, fill1, pen1), Is.False);
        Assert.That(node.Equals(rect1, fill2, pen1), Is.False);
        Assert.That(node.Equals(rect1, fill1, pen2), Is.False);
    }

    [Test]
    public void Process_ShouldReturnCorrectRenderNodeOperation()
    {
        var rect = new Rect(0, 0, 100, 100);
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var context = new RenderNodeContext(Mock.Of<IImmediateCanvasFactory>(), []);

        var node = new EllipseRenderNode(rect, fill, pen);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Null);
        Assert.That(operations.Length, Is.EqualTo(1));
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideEllipse()
    {
        var rect = new Rect(0, 0, 100, 100);
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var context = new RenderNodeContext(Mock.Of<IImmediateCanvasFactory>(), []);

        var node = new EllipseRenderNode(rect, fill, pen);
        var operations = node.Process(context);
        var point = new Point(50, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideEllipse()
    {
        var rect = new Rect(0, 0, 100, 100);
        IBrush fill = new SolidColorBrush(Colors.Red);
        IPen pen = new Pen { Brush = Brushes.Black, Thickness = 1 };
        var context = new RenderNodeContext(Mock.Of<IImmediateCanvasFactory>(), []);

        var node = new EllipseRenderNode(rect, fill, pen);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }
}
