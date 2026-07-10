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
            // Thread the parent's diagnostics/pool (not just the scale ceiling): a referenced subtree must count
            // on the owning renderer's PipelineDiagnostics (FR-017) and share its RenderTargetPool (FR-006).
            var processor = new RenderNodeProcessor(
                Child, context.IsRenderCacheEnabled, context.OutputScale, context.MaxWorkingScale,
                context.Diagnostics, context.Pool);
            return processor.PullToRoot();
        }

        return [];
    }

    protected override void OnDispose(bool disposing)
    {
        Child = null;
    }
}
