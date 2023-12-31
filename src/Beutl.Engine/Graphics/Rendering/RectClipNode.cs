namespace Beutl.Graphics.Rendering;

public sealed class RectClipNode(Rect clip, ClipOperation operation) : ContainerNode
{
    public Rect Clip { get; } = clip;

    public ClipOperation Operation { get; } = operation;

    public bool Equals(Rect clip, ClipOperation operation)
    {
        return Clip == clip
            && Operation == operation;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushClip(Clip, Operation))
        {
            base.Render(canvas);
        }
    }
}
