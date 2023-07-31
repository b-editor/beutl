namespace Beutl.Graphics.Rendering;

public sealed class RectClipNode : ContainerNode
{
    public RectClipNode(Rect clip, ClipOperation operation)
    {
        Clip = clip;
        Operation = operation;
    }

    public Rect Clip { get; }

    public ClipOperation Operation { get; }

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
