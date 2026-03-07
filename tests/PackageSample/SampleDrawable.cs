using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace PackageSample;

public sealed partial class SampleDrawable : Drawable
{
    public SampleDrawable()
    {
        ScanProperties<SampleDrawable>();
    }

    public IProperty<float> Size { get; } = Property.Create(100f);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return new(r.Size, r.Size);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        context.DrawRectangle(new(0, 0, r.Size, r.Size), Brushes.Resource.White, null);
    }
}
