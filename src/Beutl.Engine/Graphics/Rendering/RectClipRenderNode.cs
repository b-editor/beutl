namespace Beutl.Graphics.Rendering;

public sealed class RectClipRenderNode(Rect clip, ClipOperation operation) : ContainerRenderNode
{
    public Rect Clip { get; } = clip;

    public ClipOperation Operation { get; } = operation;

    public bool Equals(Rect clip, ClipOperation operation)
    {
        return Clip == clip
            && Operation == operation;
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
