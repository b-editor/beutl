using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.SourceBackdrop), ResourceType = typeof(GraphicsStrings))]
public partial class SourceBackdrop : Drawable
{
    public SourceBackdrop()
    {
        ScanProperties<SourceBackdrop>();
    }

    [Display(Name = nameof(GraphicsStrings.SourceBackdrop_Clear), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Clear { get; } = Property.CreateAnimatable(false);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return availableSize;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (resource.IsEnabled)
        {
            var r = (Resource)resource;
            var backdrop = context.Snapshot();
            if (r.Clear)
            {
                context.Clear();
            }

            Size availableSize = context.Size;
            Size size = MeasureCore(availableSize, r);

            Matrix transform = GetTransformMatrix(availableSize, size, r);
            // Same order as Drawable.Render: the opacity fades the captured image after the clear has run,
            // so Clear keeps its meaning while the drawable's own Opacity applies like any other drawable's.
            using (context.PushBlendMode(r.BlendMode))
            using (context.PushTransform(transform))
            using (context.PushOpacity(r.Opacity / 100f))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            {
                context.DrawBackdrop(backdrop);
            }
        }
    }
}
