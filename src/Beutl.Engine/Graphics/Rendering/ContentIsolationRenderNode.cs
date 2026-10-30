namespace Beutl.Graphics.Rendering;

internal sealed class ContentIsolationRenderNode(
    bool isolatesContent, bool contributesToBackdrop = true, bool preservesTargetClear = false) : ContainerRenderNode
{
    public bool IsolatesContent { get; private set; } = isolatesContent;

    public bool ContributesToBackdrop { get; private set; } = contributesToBackdrop;

    public bool PreservesTargetClear { get; private set; } = preservesTargetClear;

    public static PushedState Push(GraphicsContext2D context, bool isolatesContent = true,
        bool contributesToBackdrop = true, bool preservesTargetClear = false)
        => context.PushNode(
            (isolatesContent, contributesToBackdrop, preservesTargetClear),
            static value => new ContentIsolationRenderNode(value.isolatesContent, value.contributesToBackdrop, value.preservesTargetClear),
            static (node, value) => node.Update(value.isolatesContent, value.contributesToBackdrop, value.preservesTargetClear));

    public bool Update(bool isolatesContent, bool contributesToBackdrop = true, bool preservesTargetClear = false)
    {
        if (isolatesContent == IsolatesContent && contributesToBackdrop == ContributesToBackdrop
            && preservesTargetClear == PreservesTargetClear) return false;

        IsolatesContent = isolatesContent;
        ContributesToBackdrop = contributesToBackdrop;
        PreservesTargetClear = preservesTargetClear;
        MarkChanged();
        return true;
    }

    public override void Process(RenderNodeContext context)
    {
        if (!IsolatesContent)
        {
            context.PassThrough();
            return;
        }

        // Full-target writes have no recorded value bounds. Opaque effects can also move their output
        // beyond the recording hint, so only concrete input metadata can safely bound the surface.
        TargetRegion region = context.HasSymbolicInputTargetWrite()
            || !context.TryCalculateInputBounds(out Rect bounds)
            ? TargetRegion.Full
            : TargetRegion.Region(bounds);
        context.Publish(context.TargetLayerScope(context.Inputs, region, ContributesToBackdrop, PreservesTargetClear));
    }
}
