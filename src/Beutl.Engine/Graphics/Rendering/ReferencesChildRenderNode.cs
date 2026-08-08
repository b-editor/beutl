namespace Beutl.Graphics.Rendering;

// 単一の子ノードを参照するだけで、Disposeしないノード
public class ReferencesChildRenderNode : RenderNode
{
    // An array, not a plain field: ChildNodes hands out a span, and Update runs once per frame.
    private RenderNode[] _child;

    public ReferencesChildRenderNode(RenderNode? child)
    {
        _child = child is null ? [] : [child];
    }

    public RenderNode? Child => _child.Length == 0 ? null : _child[0];

    public override ReadOnlySpan<RenderNode> ChildNodes => _child;

    public bool Update(RenderNode? item)
    {
        if (Child != item)
        {
            HasChanges = true;
            _child = item is null ? [] : [item];
        }

        HasChanges |= item?.HasChanges == true;

        return HasChanges;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Child is { IsDisposed: false } child)
        {
            context.PublishRange(context.RecordSubtree(child));
        }
    }

    protected override void OnDispose(bool disposing)
    {
        _child = [];
    }
}
