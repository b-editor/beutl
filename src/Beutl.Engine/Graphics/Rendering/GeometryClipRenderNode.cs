using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipRenderNode(Geometry.Resource clip, ClipOperation operation) : ContainerRenderNode
{
    public (Geometry.Resource Resource, int Version)? Clip { get; private set; } = clip.Capture();

    public ClipOperation Operation { get; private set; } = operation;

    public bool Update(Geometry.Resource clip, ClipOperation operation)
    {
        bool changed = false;
        if (Clip?.Resource.GetOriginal() != clip?.GetOriginal()
            || Clip?.Version != clip?.Version)
        {
            Clip = clip.Capture();
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
        if (Clip == null)
        {
            return context.Input;
        }

        return context.Input.Select(r =>
        {
            return RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.PushClip(Clip.Value.Resource, Operation))
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
