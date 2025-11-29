using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class EllipseRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);

        var node = new EllipseRenderNode(rect, fillResource, penResource);

        Assert.That(node.Update(rect, fillResource, penResource), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(0, 0, 200, 200);
        Brush fill1 = new SolidColorBrush(Colors.Red);
        Brush fill2 = new SolidColorBrush(Colors.Blue);
        Pen pen1 = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        Pen pen2 = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        var fillResource1 = fill1.ToResource(RenderContext.Default);
        var fillResource2 = fill2.ToResource(RenderContext.Default);
        var penResource1 = pen1.ToResource(RenderContext.Default);
        var penResource2 = pen2.ToResource(RenderContext.Default);

        var node = new EllipseRenderNode(rect1, fillResource1, penResource1);

        Assert.That(node.Update(rect2, fillResource1, penResource1), Is.True);
        Assert.That(node.Update(rect1, fillResource2, penResource1), Is.True);
        Assert.That(node.Update(rect1, fillResource1, penResource2), Is.True);
    }

    [Test]
    public void Process_ShouldReturnCorrectRenderNodeOperation()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new EllipseRenderNode(rect, fillResource, penResource);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Null);
        Assert.That(operations.Length, Is.EqualTo(1));
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideEllipse()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new EllipseRenderNode(rect, fillResource, penResource);
        var operations = node.Process(context);
        var point = new Point(50, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideEllipse()
    {
        var rect = new Rect(0, 0, 100, 100);
        Brush fill = new SolidColorBrush(Colors.Red);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 1 } };
        var fillResource = fill.ToResource(RenderContext.Default);
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new EllipseRenderNode(rect, fillResource, penResource);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideEllipseStroke()
    {
        var rect = new Rect(25, 25, 75, 75);
        Pen pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 50 } };
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new EllipseRenderNode(rect, null, penResource);
        var operations = node.Process(context);
        var point = new Point(30, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }
}
