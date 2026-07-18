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
        var context = new RenderNodeContext([], RenderIntent.Delivery);
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

    [Test]
    public void OnDispose_WhenChildThrows_DetachesAndDisposesEveryChild()
    {
        var firstFailure = new InvalidOperationException("first cleanup failure");
        var node = new ContainerRenderNode();
        var first = new ThrowingChildRenderNode(firstFailure);
        var second = new ThrowingChildRenderNode(null);
        node.AddChild(first);
        node.AddChild(second);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(node.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(firstFailure));
            Assert.That(node.Children, Is.Empty);
            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.EqualTo(1));
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.True);
        });
    }

    [Test]
    public void Mutators_AfterDispose_RejectNewOwnership()
    {
        var node = new ContainerRenderNode();
        var child = new ContainerRenderNode();
        node.Dispose();

        Assert.Throws<ObjectDisposedException>(() => node.AddChild(child));
        Assert.That(child.IsDisposed, Is.False,
            "a rejected child remains owned by the caller");
        child.Dispose();
    }

    [Test]
    public void OwnershipMutators_RejectDisposedChildrenBeforeChangingOwnership()
    {
        var destination = new ContainerRenderNode();
        var current = new ContainerRenderNode();
        destination.AddChild(current);
        var disposed = new ContainerRenderNode();
        disposed.Dispose();

        Assert.Throws<ObjectDisposedException>(() => destination.AddChild(disposed));
        Assert.Throws<ObjectDisposedException>(() => destination.SetChild(0, disposed));
        Assert.That(destination.Children, Is.EqualTo(new[] { current }));

        var source = new ContainerRenderNode();
        var transferred = new ContainerRenderNode();
        source.AddChild(transferred);
        transferred.Dispose();

        Assert.Throws<ObjectDisposedException>(() => destination.BringFrom(source));
        Assert.Multiple(() =>
        {
            Assert.That(destination.Children, Is.EqualTo(new[] { current }));
            Assert.That(source.Children, Is.EqualTo(new[] { transferred }));
        });

        destination.Dispose();
        source.Dispose();
    }

    [Test]
    public void SetChild_WhenRetiredChildThrows_KeepsReplacementOwned()
    {
        var failure = new InvalidOperationException("retired child cleanup failure");
        var node = new ContainerRenderNode();
        var retired = new ThrowingChildRenderNode(failure);
        var replacement = new ThrowingChildRenderNode(null);
        node.AddChild(retired);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => node.SetChild(0, replacement));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(node.Children, Is.EqualTo(new[] { replacement }));
            Assert.That(retired.DisposeCalls, Is.EqualTo(1));
            Assert.That(replacement.DisposeCalls, Is.Zero);
        });

        node.Dispose();
        Assert.That(replacement.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void BringFrom_DisposesPreviousChildrenAndTransfersSourceChildren()
    {
        var destination = new ContainerRenderNode();
        var previous = new ThrowingChildRenderNode(null);
        destination.AddChild(previous);
        var source = new ContainerRenderNode();
        var transferred = new ThrowingChildRenderNode(null);
        source.AddChild(transferred);

        destination.BringFrom(source);

        Assert.Multiple(() =>
        {
            Assert.That(previous.DisposeCalls, Is.EqualTo(1));
            Assert.That(destination.Children, Is.EqualTo(new[] { transferred }));
            Assert.That(source.Children, Is.Empty);
            Assert.That(transferred.DisposeCalls, Is.Zero);
        });

        destination.Dispose();
        source.Dispose();
        Assert.That(transferred.DisposeCalls, Is.EqualTo(1));
    }

    private sealed class ThrowingChildRenderNode(Exception? failure) : RenderNode
    {
        public int DisposeCalls { get; private set; }

        public override RenderNodeOperation[] Process(RenderNodeContext context) => [];

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCalls++;
                if (failure != null)
                {
                    throw failure;
                }
            }
        }
    }
}
