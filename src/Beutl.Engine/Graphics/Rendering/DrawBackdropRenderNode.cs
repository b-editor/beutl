namespace Beutl.Graphics.Rendering;

public class DrawBackdropRenderNode(IBackdrop backdrop, Rect bounds) : RenderNode()
{
    public IBackdrop Backdrop { get; } = backdrop;

    public Rect Bounds { get; } = bounds;

    public bool Equals(IBackdrop backdrop, Rect bounds)
    {
        return Backdrop == backdrop && Bounds == bounds;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = false;
        return
        [
            RenderNodeOperation.CreateLambda(Bounds, canvas => Backdrop.Draw(canvas), Bounds.Contains)
        ];
    }
}
