using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RectangleRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(RenderContext.Default);

        var node = new RectangleRenderNode(rect, fill, penResource);

        Assert.That(node.Update(rect, fill, penResource), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(0, 0, 200, 200);
        var fill1 = Brushes.Resource.Red;
        var fill2 = Brushes.Resource.Blue;
        var pen1 = new Pen();
        pen1.Brush.CurrentValue = Brushes.Black;
        pen1.Thickness.CurrentValue = 1;
        var pen2 = new Pen();
        pen2.Brush.CurrentValue = Brushes.Black;
        pen2.Thickness.CurrentValue = 2;
        var penResource1 = pen1.ToResource(RenderContext.Default);
        var penResource2 = pen2.ToResource(RenderContext.Default);

        var node = new RectangleRenderNode(rect1, fill1, penResource1);

        Assert.That(node.Update(rect2, fill1, penResource1), Is.True);
        Assert.That(node.Update(rect1, fill2, penResource1), Is.True);
        Assert.That(node.Update(rect1, fill1, penResource2), Is.True);
    }

    [Test]
    public void Process_ShouldReturnCorrectRenderNodeOperation()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new RectangleRenderNode(rect, fill, penResource);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Null);
        Assert.That(operations.Length, Is.EqualTo(1));
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideRectangle()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new RectangleRenderNode(rect, fill, penResource);
        var operations = node.Process(context);
        var point = new Point(50, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }

    [Test]
    public void HitTest_ShouldReturnFalse_WhenPointIsOutsideRectangle()
    {
        var rect = new Rect(0, 0, 100, 100);
        var fill = Brushes.Resource.Red;
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 1;
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new RectangleRenderNode(rect, fill, penResource);
        var operations = node.Process(context);
        var point = new Point(150, 150);

        Assert.That(operations[0].HitTest(point), Is.False);
    }

    [Test]
    public void HitTest_ShouldReturnTrue_WhenPointIsInsideRectangleStroke()
    {
        var rect = new Rect(25, 25, 75, 75);
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 50;
        var penResource = pen.ToResource(RenderContext.Default);
        var context = new RenderNodeContext([]);

        var node = new RectangleRenderNode(rect, null, penResource);
        var operations = node.Process(context);
        var point = new Point(30, 50);

        Assert.That(operations[0].HitTest(point), Is.True);
    }
}
