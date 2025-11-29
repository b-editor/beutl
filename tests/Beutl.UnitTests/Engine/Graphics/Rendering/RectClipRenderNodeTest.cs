using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RectClipRenderNodeTest
{
    [Test]
    public void Update_ShouldReturnFalse_WhenAllPropertiesMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Update(rect, operation), Is.False);
    }

    [Test]
    public void Update_ShouldReturnTrue_WhenPropertiesDoNotMatch()
    {
        var rect = new Rect(0, 0, 100, 100);
        var operation = ClipOperation.Intersect;
        var node = new RectClipRenderNode(rect, operation);

        Assert.That(node.Update(default, operation), Is.True);
    }

    [Test]
    public void Process_WithoutInput_ShouldReturnEmptyRenderNodeOperation()
    {
        var context = new RenderNodeContext([]);

        var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        var operations = node.Process(context);

        Assert.That(operations, Is.Empty);
    }

    [Test]
    public void Process_WithInput_ShouldReturnExpectedRenderNodeOperation()
    {
        var context = new RenderNodeContext([
            RenderNodeOperation.CreateLambda(default, _ => {  })
        ]);

        var node = new RectClipRenderNode(new Rect(0, 0, 100, 100), ClipOperation.Intersect);
        var operations = node.Process(context);

        Assert.That(operations, Is.Not.Empty);
    }
}
