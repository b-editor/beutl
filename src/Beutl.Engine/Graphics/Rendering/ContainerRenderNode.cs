using System.Runtime.ExceptionServices;

namespace Beutl.Graphics.Rendering;

public class ContainerRenderNode : RenderNode
{
    private readonly List<RenderNode> _children = [];

    public IReadOnlyList<RenderNode> Children => _children;

    public void AddChild(RenderNode item)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateOwnedChild(item);
        _children.Add(item);
    }

    public void RemoveChild(RenderNode item)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(item);
        _children.Remove(item);
    }

    public void RemoveRange(int index, int count)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _children.RemoveRange(index, count);
    }

    public void SetChild(int index, RenderNode item)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateOwnedChild(item);
        RenderNode old = _children[index];
        if (ReferenceEquals(old, item))
            return;

        _children[index] = item;
        old.Dispose();
    }

    public void BringFrom(ContainerRenderNode containerNode)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(containerNode);
        ObjectDisposedException.ThrowIf(containerNode.IsDisposed, containerNode);
        if (ReferenceEquals(this, containerNode))
            return;

        RenderNode[] previousChildren = [.. _children];
        RenderNode[] transferredChildren = [.. containerNode._children];
        foreach (RenderNode child in transferredChildren)
        {
            ValidateOwnedChild(child);
        }

        _children.Clear();
        _children.AddRange(transferredChildren);
        containerNode._children.Clear();

        var transferred = new HashSet<RenderNode>(transferredChildren, ReferenceEqualityComparer.Instance);
        DisposeChildren(previousChildren.Where(child => !transferred.Contains(child)));
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input;
    }

    protected override void OnDispose(bool disposing)
    {
        RenderNode[] children = [.. _children];
        _children.Clear();
        DisposeChildren(children);
    }

    private static void DisposeChildren(IEnumerable<RenderNode> children)
    {
        Exception? failure = null;
        foreach (RenderNode item in children)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void ValidateOwnedChild(RenderNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ObjectDisposedException.ThrowIf(item.IsDisposed, item);
    }
}
