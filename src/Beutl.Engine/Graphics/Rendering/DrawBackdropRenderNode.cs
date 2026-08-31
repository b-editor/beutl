namespace Beutl.Graphics.Rendering;

public class DrawBackdropRenderNode(IBackdrop backdrop, Rect bounds) : RenderNode()
{
    private static readonly RenderResourceSlot<IBackdrop> s_backdropSlot = new();
    private static readonly RenderResourceSlot[] s_slots = [s_backdropSlot];

    public IBackdrop Backdrop { get; private set; } = backdrop;

    public Rect Bounds { get; private set; } = bounds;

    public bool Update(IBackdrop backdrop, Rect bounds)
    {
        if (Backdrop != backdrop || Bounds != bounds)
        {
            Backdrop = backdrop;
            Bounds = bounds;
            MarkChanged();
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
            context.Publish(context.TargetCommand(
                [capture!],
                TargetCommandDescription.Create(
                    default(BackdropCaptureState),
                    static (session, _) => session.Canvas.Use(canvas => session.Inputs[0].Draw(canvas)),
                    TargetRegion.Region(bounds),
                    bounds,
                    hitTest)));
            return;
        }

        RenderResource<IBackdrop> resource = context.Borrow(backdrop);
        context.Publish(context.RawTargetCommand(
            RawTargetCommandDescription.Create(
                new RawBackdropCommandState(resource),
                static (session, state) => session.UseResource(
                    state.Resource,
                    value => value.Draw(session.Canvas)),
                bounds,
                hitTest,
                resources: [s_backdropSlot.Bind(resource)],
                slots: s_slots)));
    }

    private readonly record struct BackdropCaptureState;

    private readonly record struct RawBackdropCommandState(RenderResource<IBackdrop> Resource);
}
