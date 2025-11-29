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

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input.Select(r =>
        {
            return RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.PushClip(Clip, Operation))
                {
                    r.Render(canvas);
                }
            });
        }).ToArray();
    }
}
