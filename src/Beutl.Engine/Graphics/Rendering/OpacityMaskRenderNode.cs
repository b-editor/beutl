using Beutl.Engine;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class OpacityMaskRenderNode(Brush.Resource mask, Rect maskBounds, bool invert) : ContainerRenderNode
{
    public (Brush.Resource Resource, int Version)? Mask { get; private set; } = mask.Capture();

    public Rect MaskBounds { get; private set; } = maskBounds;

    public bool Invert { get; private set; } = invert;

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
            MarkChanged();
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
        RenderResource<Brush.Resource> maskResource = context.Borrow(mask.Resource);
        context.PublishMappedInputs(
            (maskResource, maskBounds, invert),
            static (context, input, state) => context.OpacityMask(
                input,
                state.maskResource,
                state.maskBounds,
                state.invert));
    }

    // Whether the mask leaves less than half the opacity at a point, so that along a transition's soft edge the
    // side mostly shown answers. A brush that draws something needs a render pass, so it hides nothing here.
    internal bool HidesPoint(Point point)
    {
        if (Mask is not { Resource: GradientBrush.Resource or SolidColorBrush.Resource } mask) return false;

        using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Translate(0.5f - point.X, 0.5f - point.Y);
        using (var paint = new SKPaint())
        {
            new BrushConstructor(MaskBounds, mask.Resource, BlendMode.SrcOver, RenderIntent.Preview, null)
                .ConfigurePaint(paint);
            surface.Canvas.DrawPaint(paint);
        }

        using SKPixmap pixels = surface.PeekPixels();
        float opacity = pixels.GetPixelColor(0, 0).Alpha / 255f;
        return (Invert ? 1 - opacity : opacity) < 0.5f;
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Mask = null!;
    }
}
