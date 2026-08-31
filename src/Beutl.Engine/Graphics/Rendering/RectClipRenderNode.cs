namespace Beutl.Graphics.Rendering;

public sealed class RectClipRenderNode(Rect clip, ClipOperation operation) : ContainerRenderNode
{
    public Rect Clip { get; private set; } = clip;

    public ClipOperation Operation { get; private set; } = operation;

    public bool Update(Rect clip, ClipOperation operation)
    {
        bool changed = false;
        if (Clip != clip)
        {
            Clip = clip;
            changed = true;
        }

        if (Operation != operation)
        {
            Operation = operation;
            changed = true;
        }

        if (changed)
        {
            MarkChanged();
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        context.PublishMappedInputs(
            TargetScopeDescription.Create(
                new RectClipMetadata(Clip, Operation),
                static (session, state) => session.Canvas.Use(canvas =>
                {
                    using (canvas.PushClip(state.Clip, state.Operation))
                    {
                        session.ReplayInput();
                    }
                }),
                RenderBoundsContract.Create(TransformBounds, GetRequiredInputBounds),
                RenderHitTestContract.Custom(HitTest),
                RenderScaleContract.PreserveInputSupply,
                deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
                deviceGridMapping: RenderDeviceGridMapping.Preserved),
            static (context, input, value) => context.TargetScope(input, value));
    }

    private Rect TransformBounds(Rect value)
        => Operation == ClipOperation.Intersect ? value.Intersect(Clip) : value;

    private Rect GetRequiredInputBounds(Rect value)
        => Operation == ClipOperation.Intersect ? value.Intersect(Clip) : value;

    private bool HitTest(RenderHitTestContext context, Point point)
    {
        bool insideClip = Clip.Contains(point);
        bool clipAcceptsPoint = Operation == ClipOperation.Intersect ? insideClip : !insideClip;
        return clipAcceptsPoint && context.Inputs.Any(input => input.HitTest(point));
    }

    private readonly record struct RectClipMetadata(Rect Clip, ClipOperation Operation);
}
