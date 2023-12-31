using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering;

public class ContainerNode : IGraphicNode
{
    private readonly List<IGraphicNode> _children = [];
    private bool _isBoundsDirty = true;
    private Rect _originalBounds;

    ~ContainerNode()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public Rect OriginalBounds
    {
        get
        {
            //if (_isBoundsDirty)
            {
                _originalBounds = default;
                foreach (IGraphicNode child in _children)
                {
                    _originalBounds = _originalBounds.Union(child.Bounds);
                }

                _originalBounds = _originalBounds.Normalize();
                _isBoundsDirty = false;
            }

            return _originalBounds;
        }
    }

    public Rect Bounds => TransformBounds(OriginalBounds);

    public IReadOnlyList<IGraphicNode> Children => _children;

    public bool IsDisposed { get; private set; }

    public virtual bool HitTest(Point point)
    {
        foreach (IGraphicNode child in Children)
        {
            if (child.HitTest(point))
                return true;
        }

        return false;
    }

    public virtual void Render(ImmediateCanvas canvas)
    {
        foreach (IGraphicNode item in _children)
        {
            canvas.DrawNode(item);
        }
    }

    protected virtual Rect TransformBounds(Rect bounds)
    {
        return bounds;
    }

    public void AddChild(IGraphicNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _children.Add(item);
        _isBoundsDirty = true;
    }

    public void RemoveChild(IGraphicNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _children.Remove(item);
        _isBoundsDirty = true;
    }

    public void RemoveRange(int index, int count)
    {
        _children.RemoveRange(index, count);
        _isBoundsDirty = _isBoundsDirty || count > 0;
    }

    public void SetChild(int index, IGraphicNode item)
    {
        _children[index]?.Dispose();
        _children[index] = item;
        _isBoundsDirty = true;
    }

    public void BringFrom(ContainerNode containerNode)
    {
        _children.Clear();
        _children.AddRange(containerNode._children);

        containerNode._children.Clear();
        _isBoundsDirty = true;
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
        foreach (IGraphicNode? item in CollectionsMarshal.AsSpan(_children))
        {
            item.Dispose();
        }

        _children.Clear();
    }
}
