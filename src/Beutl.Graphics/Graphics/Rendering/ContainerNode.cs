namespace Beutl.Graphics.Rendering;

public class ContainerNode : IGraphicNode
{
    private readonly List<IGraphicNode> _children = new List<IGraphicNode>();
    private bool _isBoundsDirty = true;
    private Rect _originalBounds;

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

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

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
}
