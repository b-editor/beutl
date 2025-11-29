using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class ContainerRenderNodeTest
{
    [Test]
    public void AddChild_ShouldAddChild()
    {
        var node = new ContainerRenderNode();
        var child = new ContainerRenderNode();
        node.AddChild(child);

        Assert.That(node.Children, Contains.Item(child));
    }

    [Test]
    public void RemoveChild_ShouldRemoveChild()
    {
        var node = new ContainerRenderNode();
        var child = new ContainerRenderNode();
        node.AddChild(child);
        node.RemoveChild(child);

        Assert.That(node.Children, Does.Not.Contain(child));
    }

    [Test]
    public void RemoveRange_ShouldRemoveRangeOfChildren()
    {
        var node = new ContainerRenderNode();
        var child1 = new ContainerRenderNode();
        var child2 = new ContainerRenderNode();
        node.AddChild(child1);
        node.AddChild(child2);

        node.RemoveRange(0, 2);

        Assert.That(node.Children, Is.Empty);
    }

    [Test]
    public void SetChild_ShouldReplaceChildAtIndex()
    {
        var node = new ContainerRenderNode();
        var child1 = new ContainerRenderNode();
        var child2 = new ContainerRenderNode();
        node.AddChild(child1);

        node.SetChild(0, child2);

        Assert.That(node.Children[0], Is.EqualTo(child2));
    }

    [Test]
    public void Process_ShouldReturnContextInput()
    {
        var node = new ContainerRenderNode();
        var context = new RenderNodeContext([]);
        var result = node.Process(context);

        Assert.That(result, Is.EqualTo(context.Input));
    }

    [Test]
    public void OnDispose_ShouldDisposeAllChildren()
    {
        var node = new ContainerRenderNode();
        var child1 = new ContainerRenderNode();
        var child2 = new ContainerRenderNode();
        node.AddChild(child1);
        node.AddChild(child2);

        node.Dispose();

        Assert.That(node.Children, Is.Empty);
        Assert.That(node.IsDisposed, Is.True);
        Assert.That(child1.IsDisposed, Is.True);
        Assert.That(child2.IsDisposed, Is.True);
    }
}
