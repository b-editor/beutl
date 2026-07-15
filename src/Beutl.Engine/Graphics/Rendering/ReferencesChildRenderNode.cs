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

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (Child != null && !Child.IsDisposed)
        {
            // A referenced subtree inherits the complete pull policy and the executor-owned resources.
            var processor = context.CreateChildProcessor(Child, context.IsRenderCacheEnabled);
            return processor.PullToRoot();
        }

        return [];
    }

    protected override void OnDispose(bool disposing)
    {
        Child = null;
    }
}
