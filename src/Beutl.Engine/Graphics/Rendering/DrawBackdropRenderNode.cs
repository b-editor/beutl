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
        if (backdrop.GetType() == typeof(SnapshotBackdropRenderNode))
        {
            RenderFragmentHandle capture = context.BuiltInBackdrop(backdrop);
            TargetCommandDescription description = TargetCommandDescription.Create(
                static session => session.Canvas.Use(canvas => session.Inputs[0].Draw(canvas)),
                TargetRegion.Region(bounds),
                bounds,
                RenderHitTestContract.OutputBounds,
                TargetAccess.ReadWrite,
                structuralKey: typeof(DrawBackdropRenderNode),
                runtimeIdentity: new RenderRuntimeIdentity(bounds));
            context.Publish(context.TargetCommand([capture], description));
            return;
        }

        RenderResource<IBackdrop> resource = context.Borrow(backdrop);
        RawTargetCommandDescription rawDescription = RawTargetCommandDescription.Create(
            session => session.UseResource(resource, value => value.Draw(session.Canvas)),
            bounds,
            RenderHitTestContract.OutputBounds,
            structuralKey: typeof(IBackdrop),
            resources: [resource]);
        context.Publish(context.RawTargetCommand(rawDescription));
    }
}
