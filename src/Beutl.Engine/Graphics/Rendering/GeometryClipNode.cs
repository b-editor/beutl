using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipNode(Geometry clip, ClipOperation operation) : ContainerNode
{
    private readonly int _version = clip.Version;

    public Geometry Clip { get; private set; } = clip;

    public ClipOperation Operation { get; } = operation;

    public bool Equals(Geometry clip, ClipOperation operation)
    {
        return Clip == clip
            && _version == clip.Version
            && Operation == operation;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushClip(Clip, Operation))
        {
            base.Render(canvas);
        }
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Clip = null!;
    }
}
