using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipNode : ContainerNode
{
    public GeometryClipNode(Geometry clip, ClipOperation operation)
    {
        Clip = clip;
        Operation = operation;
    }

    public Geometry Clip { get; private set; }

    public ClipOperation Operation { get; }

    public bool Equals(Geometry clip, ClipOperation operation)
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

    public override void Dispose()
    {
        Clip = null!;
    }
}
