using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityMaskNode(IBrush mask, Rect maskBounds, bool invert) : ContainerNode
{
    public IBrush Mask { get; private set; } = (mask as IMutableBrush)?.ToImmutable() ?? mask;

    public Rect MaskBounds { get; } = maskBounds;

    public bool Invert { get; } = invert;

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
