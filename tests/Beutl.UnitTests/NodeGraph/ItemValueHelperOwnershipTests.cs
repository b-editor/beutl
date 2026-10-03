using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class ItemValueHelperOwnershipTests
{
    [Test]
    public void AcceptMatrix_ConvertingAfterBorrowedTransform_DoesNotMutateBorrowedValue()
    {
        var owner = new TestHierarchy();
        var consumer = new TestHierarchy();
        var borrowed = new MatrixTransform(Matrix.CreateTranslation(1, 2));
        ((IModifiableHierarchical)owner).AddChild(borrowed);
        using var value = new ItemValue<Transform?>();
        value.AcceptMatrix(consumer);
        var source = new ItemValue<object?> { Value = borrowed };
        Assert.That(value.PropagateFrom(source), Is.EqualTo(PropagateResult.Converted));
        Matrix replacement = Matrix.CreateTranslation(3, 4);
        source.Value = replacement;

        Assert.That(value.PropagateFrom(source), Is.EqualTo(PropagateResult.Converted));

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.Not.SameAs(borrowed));
            Assert.That(value.Value!.HierarchicalParent, Is.SameAs(consumer));
            Assert.That(((MatrixTransform)value.Value).Matrix.CurrentValue, Is.EqualTo(replacement));
            Assert.That(borrowed.Matrix.CurrentValue, Is.EqualTo(Matrix.CreateTranslation(1, 2)));
            Assert.That(borrowed.HierarchicalParent, Is.SameAs(owner));
        });
    }

    [Test]
    public void AcceptMatrix_Dispose_DoesNotDetachBorrowedTransform()
    {
        var hierarchy = new TestHierarchy();
        var borrowed = new MatrixTransform();
        ((IModifiableHierarchical)hierarchy).AddChild(borrowed);
        var value = new ItemValue<Transform?>();
        value.AcceptMatrix(hierarchy);
        value.PropagateFrom(new ItemValue<object?> { Value = borrowed });

        value.Dispose();

        Assert.That(borrowed.HierarchicalParent, Is.SameAs(hierarchy));
    }

    [Test]
    public void AcceptMatrix_TypedReplacement_DisposeDetachesOnlyCreatedWrapper()
    {
        var hierarchy = new TestHierarchy();
        var borrowed = new MatrixTransform();
        ((IModifiableHierarchical)hierarchy).AddChild(borrowed);
        var value = new ItemValue<Transform?>();
        value.AcceptMatrix(hierarchy);
        value.PropagateFrom(new ItemValue<Matrix> { Value = Matrix.Identity });
        Transform created = value.Value!;

        Assert.That(value.PropagateFrom(new ItemValue<Transform?> { Value = borrowed }),
            Is.EqualTo(PropagateResult.Success));
        value.Dispose();
        value.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(created.HierarchicalParent, Is.Null);
            Assert.That(borrowed.HierarchicalParent, Is.SameAs(hierarchy));
            Assert.That(((IHierarchical)hierarchy).HierarchicalChildren, Is.EqualTo(new[] { borrowed }));
        });
    }

    [Test]
    public void AcceptMatrix_RepeatedConversion_ReusesCreatedWrapper()
    {
        var hierarchy = new TestHierarchy();
        using var value = new ItemValue<Transform?>();
        value.AcceptMatrix(hierarchy);
        value.PropagateFrom(new ItemValue<Matrix> { Value = Matrix.Identity });
        Transform created = value.Value!;
        Matrix replacement = Matrix.CreateTranslation(3, 4);

        value.PropagateFrom(new ItemValue<Matrix> { Value = replacement });

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.SameAs(created));
            Assert.That(((MatrixTransform)created).Matrix.CurrentValue, Is.EqualTo(replacement));
            Assert.That(((IHierarchical)hierarchy).HierarchicalChildren, Is.EqualTo(new[] { created }));
        });
    }

    [Test]
    public void AcceptNode_ConvertingAfterBorrowedDrawable_DoesNotMutateBorrowedValue()
    {
        var owner = new TestHierarchy();
        var consumer = new TestHierarchy();
        using var original = new ContainerRenderNode();
        using var replacement = new ContainerRenderNode();
        var borrowed = new RenderNodeDrawable { GraphNode = original };
        ((IModifiableHierarchical)owner).AddChild(borrowed);
        using var value = new ItemValue<Drawable?>();
        value.AcceptNode(consumer);
        var source = new ItemValue<object?> { Value = borrowed };
        Assert.That(value.PropagateFrom(source), Is.EqualTo(PropagateResult.Converted));
        source.Value = replacement;

        Assert.That(value.PropagateFrom(source), Is.EqualTo(PropagateResult.Converted));

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.Not.SameAs(borrowed));
            Assert.That(value.Value!.HierarchicalParent, Is.SameAs(consumer));
            Assert.That(((RenderNodeDrawable)value.Value).GraphNode, Is.SameAs(replacement));
            Assert.That(borrowed.GraphNode, Is.SameAs(original));
            Assert.That(borrowed.HierarchicalParent, Is.SameAs(owner));
        });
    }

    [Test]
    public void AcceptNode_Dispose_DoesNotDetachBorrowedDrawable()
    {
        var hierarchy = new TestHierarchy();
        var borrowed = new RenderNodeDrawable();
        ((IModifiableHierarchical)hierarchy).AddChild(borrowed);
        var value = new ItemValue<Drawable?>();
        value.AcceptNode(hierarchy);
        value.PropagateFrom(new ItemValue<object?> { Value = borrowed });

        value.Dispose();

        Assert.That(borrowed.HierarchicalParent, Is.SameAs(hierarchy));
    }

    [Test]
    public void AcceptNode_TypedReplacement_DisposeDetachesOnlyCreatedWrapper()
    {
        var hierarchy = new TestHierarchy();
        var borrowed = new RenderNodeDrawable();
        ((IModifiableHierarchical)hierarchy).AddChild(borrowed);
        using var node = new ContainerRenderNode();
        var value = new ItemValue<Drawable?>();
        value.AcceptNode(hierarchy);
        value.PropagateFrom(new ItemValue<RenderNode?> { Value = node });
        Drawable created = value.Value!;

        Assert.That(value.PropagateFrom(new ItemValue<Drawable?> { Value = borrowed }),
            Is.EqualTo(PropagateResult.Success));
        value.Dispose();
        value.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(created.HierarchicalParent, Is.Null);
            Assert.That(borrowed.HierarchicalParent, Is.SameAs(hierarchy));
            Assert.That(((IHierarchical)hierarchy).HierarchicalChildren, Is.EqualTo(new[] { borrowed }));
            Assert.That(node.IsDisposed, Is.False);
        });
    }

    [Test]
    public void AcceptNode_RepeatedConversion_ReusesCreatedWrapper()
    {
        var hierarchy = new TestHierarchy();
        using var first = new ContainerRenderNode();
        using var second = new ContainerRenderNode();
        using var value = new ItemValue<Drawable?>();
        value.AcceptNode(hierarchy);
        value.PropagateFrom(new ItemValue<RenderNode?> { Value = first });
        Drawable created = value.Value!;

        value.PropagateFrom(new ItemValue<RenderNode?> { Value = second });

        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.SameAs(created));
            Assert.That(((RenderNodeDrawable)created).GraphNode, Is.SameAs(second));
            Assert.That(((IHierarchical)hierarchy).HierarchicalChildren, Is.EqualTo(new[] { created }));
            Assert.That(first.IsDisposed, Is.False);
            Assert.That(second.IsDisposed, Is.False);
        });
    }

    private sealed class TestHierarchy : Hierarchical;
}
