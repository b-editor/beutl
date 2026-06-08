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
            // feature 003 (FR-037): forward the working-scale ceiling into the nested pull, else the child
            // subtree falls back to +inf and a high-density source there escapes the preview cap.
            var processor = new RenderNodeProcessor(
                Child, context.IsRenderCacheEnabled, context.OutputScale, context.MaxWorkingScale);
            return processor.PullToRoot();
        }

        return [];
    }

    protected override void OnDispose(bool disposing)
    {
        Child = null;
    }
}
