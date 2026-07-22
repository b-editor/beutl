using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityMaskRenderNode(Brush.Resource mask, Rect maskBounds, bool invert) : ContainerRenderNode
{
    public (Brush.Resource Resource, int Version)? Mask { get; set; } = mask.Capture();

    public Rect MaskBounds { get; set; } = maskBounds;

    public bool Invert { get; set; } = invert;

    public bool Update(Brush.Resource? mask, Rect maskBounds, bool invert)
    {
        bool changed = false;
        if (!mask.Compare(Mask))
        {
            Mask = mask.Capture();
            changed = true;
        }

        if (MaskBounds != maskBounds)
        {
            MaskBounds = maskBounds;
            changed = true;
        }

        if (Invert != invert)
        {
            Invert = invert;
            changed = true;
        }

        if (changed)
        {
            HasChanges = true;
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Mask is not { } mask)
        {
            return;
        }

        Rect maskBounds = MaskBounds;
        bool invert = Invert;
        foreach (RenderFragmentHandle input in context.Inputs)
        {
            context.Publish(context.OpacityMask(input, mask.Resource, maskBounds, invert));
        }
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Mask = null!;
    }
}
