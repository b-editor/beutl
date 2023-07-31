using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipNode : ContainerNode
{
    private readonly int _version;

    public GeometryClipNode(Geometry clip, ClipOperation operation)
    {
        Clip = clip;
        _version = clip.Version;
        Operation = operation;
    }

    public Geometry Clip { get; private set; }

    public ClipOperation Operation { get; }

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
