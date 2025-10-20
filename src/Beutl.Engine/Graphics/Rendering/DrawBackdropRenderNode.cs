namespace Beutl.Graphics.Rendering;

public class DrawBackdropRenderNode(IBackdrop backdrop, Rect bounds) : RenderNode()
{
    public IBackdrop Backdrop { get; private set; } = backdrop;

    public Rect Bounds { get; private set; } = bounds;

    public bool Update(IBackdrop backdrop, Rect bounds)
    {
        if (Backdrop != backdrop || Bounds != bounds)
        {
            Backdrop = backdrop;
            Bounds = bounds;
            HasChanges = true;
            return true;
        }

        return false;
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
