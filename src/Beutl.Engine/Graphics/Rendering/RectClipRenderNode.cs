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

        HasChanges = true;
        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        Rect clip = Clip;
        ClipOperation operation = Operation;
        var metadata = new RectClipMetadata(clip, operation);
        TargetScopeDescription description = TargetScopeDescription.Create(
            session => session.Canvas.Use(canvas =>
            {
                using (canvas.PushClip(clip, operation))
                {
                    session.ReplayInput();
                }
            }),
            RenderBoundsContract.Create(
                metadata.TransformBounds,
                metadata.GetRequiredInputBounds,
                structuralKey: (typeof(RectClipRenderNode), "clip-bounds")),
            RenderHitTestContract.Custom(
                metadata.HitTest,
                structuralKey: typeof(RectClipRenderNode)),
            RenderScaleContract.PreserveInputSupply,
            structuralKey: typeof(RectClipRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity((clip, operation)));

        foreach (RenderFragmentHandle input in context.Inputs)
        {
            context.Publish(context.TargetScope(input, description));
        }
    }

    private readonly record struct RectClipMetadata(Rect Clip, ClipOperation Operation)
    {
        public Rect TransformBounds(Rect value)
            => Operation == ClipOperation.Intersect ? value.Intersect(Clip) : value;

        public Rect GetRequiredInputBounds(Rect value)
            => Operation == ClipOperation.Intersect ? value.Intersect(Clip) : value;

        public bool HitTest(RenderHitTestContext context, Point point)
        {
            bool insideClip = Clip.Contains(point);
            bool clipAcceptsPoint = Operation == ClipOperation.Intersect ? insideClip : !insideClip;
            return clipAcceptsPoint && context.Inputs.Any(input => input.HitTest(point));
        }
    }
}
