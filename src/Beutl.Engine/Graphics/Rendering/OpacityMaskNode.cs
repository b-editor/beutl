using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityMaskNode : ContainerNode
{
    public OpacityMaskNode(IBrush mask, Rect maskBounds, bool invert)
    {
        Mask = (mask as IMutableBrush)?.ToImmutable() ?? mask;
        MaskBounds = maskBounds;
        Invert = invert;
    }

    public IBrush Mask { get; private set; }

    public Rect MaskBounds { get; }

    public bool Invert { get; }

    public bool Equals(IBrush? mask, Rect maskBounds, bool invert)
    {
        return EqualityComparer<IBrush?>.Default.Equals(Mask, mask)
            && MaskBounds == maskBounds
            && Invert == invert;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushOpacityMask(Mask, MaskBounds, Invert))
        {
            base.Render(canvas);
        }
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Mask = null!;
    }
}
