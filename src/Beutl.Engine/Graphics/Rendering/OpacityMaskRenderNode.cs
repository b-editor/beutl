using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityMaskRenderNode(IBrush mask, Rect maskBounds, bool invert) : ContainerRenderNode
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

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input.Select(r =>
        {
            return RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.PushOpacityMask(Mask, MaskBounds, Invert))
                {
                    r.Render(canvas);
                }
            });
        }).ToArray();
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Mask = null!;
    }
}
