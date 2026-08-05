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

    public override void Process(RenderNodeContext context)
    {
        context.DisableRenderCache();

        IBackdrop backdrop = Backdrop;
        Rect bounds = Bounds;
        // A zero-area canvas replays no pixels, so the backdrop covers no point a query can reach.
        RenderHitTestContract hitTest = bounds.Width > 0 && bounds.Height > 0
            ? RenderHitTestContract.OutputBounds
            : RenderHitTestContract.None;
        if (context.TryBuiltInBackdrop(backdrop, out RenderFragmentHandle? capture))
        {
            TargetCommandDescription description = TargetCommandDescription.Create(
                static session => session.Canvas.Use(canvas => session.Inputs[0].Draw(canvas)),
                TargetRegion.Region(bounds),
                bounds,
                hitTest,
                runtimeIdentity: new RenderRuntimeIdentity(bounds));
            context.Publish(context.TargetCommand([capture!], description));
            return;
        }

        RenderResource<IBackdrop> resource = context.Borrow(backdrop);
        RawTargetCommandDescription rawDescription = RawTargetCommandDescription.Create(
            session => session.UseResource(resource, value => value.Draw(session.Canvas)),
            bounds,
            hitTest,
            resources: [resource]);
        context.Publish(context.RawTargetCommand(rawDescription));
    }
}
