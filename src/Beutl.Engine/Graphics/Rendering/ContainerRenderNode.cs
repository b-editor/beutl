using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering;

public class ContainerRenderNode : RenderNode
{
    private readonly List<RenderNode> _children = [];

    public IReadOnlyList<RenderNode> Children => _children;

    public override ReadOnlySpan<RenderNode> ChildNodes => CollectionsMarshal.AsSpan(_children);

    public void AddChild(RenderNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _children.Add(item);
        HasChanges = true;
    }

    public void RemoveChild(RenderNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_children.Remove(item))
            HasChanges = true;
    }

    public void RemoveRange(int index, int count)
    {
        _children.RemoveRange(index, count);
        if (count > 0)
            HasChanges = true;
    }

    /// <summary>Replaces the child at <paramref name="index"/> and disposes the one it replaced.</summary>
    /// <remarks>
    /// Passing the child already at that index is a no-op rather than a self-replacement: disposing the
    /// previous child after storing the new one would otherwise leave a disposed node in the container.
    /// </remarks>
    public void SetChild(int index, RenderNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RenderNode? previous = _children[index];
        if (ReferenceEquals(previous, item))
            return;

        _children[index] = item;
        HasChanges = true;
        previous?.Dispose();
    }

    public void BringFrom(ContainerRenderNode containerNode)
    {
        _children.Clear();
        _children.AddRange(containerNode._children);

        containerNode._children.Clear();
        HasChanges = true;
        containerNode.HasChanges = true;
    }

    public override void Process(RenderNodeContext context)
    {
        context.PassThrough();
    }

    protected override void OnDispose(bool disposing)
    {
        foreach (RenderNode? item in CollectionsMarshal.AsSpan(_children))
        {
            item.Dispose();
        }

        _children.Clear();
    }
}
