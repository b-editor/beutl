using Beutl.Graphics.Filters;

namespace Beutl.Graphics.Rendering;

[Obsolete("Use FilterEffectNode")]
public sealed class ImageFilterNode : ContainerNode
{
    public ImageFilterNode(IImageFilter imageFilter, Rect filterBounds)
    {
        ImageFilter = imageFilter;
        FilterBounds = filterBounds;
    }

    public IImageFilter ImageFilter { get; private set; }

    public Rect FilterBounds { get; }

    public bool Equals(IImageFilter imageFilter, Rect filterBounds)
    {
        return ImageFilter == imageFilter
            && FilterBounds == filterBounds;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushImageFilter(ImageFilter, FilterBounds))
        {
            base.Render(canvas);
        }
    }

    public override void Dispose()
    {
        ImageFilter = null!;
    }
}
