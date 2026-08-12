using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

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
    public void SetChild_CommitsReplacementBeforeOldChildDisposalFailure()
    {
        using var node = new ContainerRenderNode();
        var oldChild = new ThrowingDisposeRenderNode();
        var replacement = new ContainerRenderNode();
        node.AddChild(oldChild);

        Assert.That(
            () => node.SetChild(0, replacement),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("child disposal failed"));
        Assert.That(node.Children[0], Is.SameAs(replacement));
        Assert.That(replacement.IsDisposed, Is.False);
    }

    [Test]
    public void Measure_ShouldPassThroughChildOutput()
    {
        var node = new ContainerRenderNode();
        var bounds = new Rect(5, 10, 20, 30);
        node.AddChild(new RectangleRenderNode(bounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement result = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(result.HasFragments, Is.True);
            Assert.That(result.HasContributingValues, Is.True);
            Assert.That(result.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
            Assert.That(result.OutputBounds, Is.EqualTo(bounds));
        });
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

    private sealed class ThrowingDisposeRenderNode : RenderNode
    {
        private bool _hasThrown;

        public override void Process(RenderNodeContext context)
            => context.PassThrough();

        protected override void OnDispose(bool disposing)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new InvalidOperationException("child disposal failed");
            }
        }
    }
}
