using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Backdrop), ResourceType = typeof(Strings))]
public partial class SourceBackdrop : Drawable
{
    public SourceBackdrop()
    {
        ScanProperties<SourceBackdrop>();
    }

    [Display(Name = nameof(Strings.ClearBuffer), ResourceType = typeof(Strings))]
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
        if (IsEnabled)
        {
            var r = (Resource)resource;
            var backdrop = context.Snapshot();
            if (r.Clear)
            {
                context.Clear();
            }

            Size availableSize = context.Size.ToSize(1);
            Size size = MeasureCore(availableSize, r);

            Matrix transform = GetTransformMatrix(availableSize, size, r);
            using (context.PushBlendMode(r.BlendMode))
            using (context.PushTransform(transform))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            {
                context.DrawBackdrop(backdrop);
            }
        }
    }
}
