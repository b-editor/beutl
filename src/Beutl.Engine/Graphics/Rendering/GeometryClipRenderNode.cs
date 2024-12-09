using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipRenderNode(Geometry clip, ClipOperation operation) : ContainerRenderNode
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

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Clip = null!;
    }
}
