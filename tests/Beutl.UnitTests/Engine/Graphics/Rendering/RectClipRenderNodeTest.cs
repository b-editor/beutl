using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RectClipRenderNodeTest
{
    [Test]
    public void Equals_ShouldReturnTrue_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Equals(rect, operation), Is.True);
    }

    [Test]
    public void Equals_ShouldReturnFalse_WhenPropertiesDoNotMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Equals(default, operation), Is.False);
    }

    [Test]
    public void Process_WithoutInput_ShouldReturnEmptyRenderNodeOperation()
    {
        var context = new RenderNodeContext(Mock.Of<IImmediateCanvasFactory>(), []);

        var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        var operations = node.Process(context);

        Assert.That(operations, Is.Empty);
    }

    [Test]
    public void Process_WithInput_ShouldReturnExpectedRenderNodeOperation()
    {
        var context = new RenderNodeContext(Mock.Of<IImmediateCanvasFactory>(), [
            RenderNodeOperation.CreateLambda(default, _ => {  })
        ]);

        var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }
}
