namespace Beutl.Graphics.Rendering;

// 単一の子ノードを参照するだけで、Disposeしないノード
public class ReferencesChildRenderNode(RenderNode? child) : RenderNode
{
    public RenderNode? Child { get; private set; } = child;

    public bool Update(RenderNode? item)
    {
        if (Child != item)
        {
            HasChanges = true;
        }

        HasChanges |= item?.HasChanges == true;
        Child = item;

        return HasChanges;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Child != null && !Child.IsDisposed)
        {
            context.PublishRange(context.RecordSubtree(Child));
        }
    }

    protected override void OnDispose(bool disposing)
    {
        Child = null;
    }
}
