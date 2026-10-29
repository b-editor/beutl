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
    public IExpressionProperty<bool> Clear { get; } = Property.CreateAnimatable(false);

    public new partial class Resource
    {
        protected override Size MeasureCore(Size availableSize)
        {
            return availableSize;
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
        }

        protected override void RenderCore(GraphicsContext2D context)
        {
            var backdrop = context.Snapshot();
            if (Clear)
            {
                context.Clear();
            }

            Size availableSize = context.Size;
            Size size = MeasureCore(availableSize);

            Matrix transform = GetTransformMatrix(availableSize, size);
            // Same order as Drawable.Resource.RenderCore: the opacity fades the captured image after the clear has run,
            // so Clear keeps its meaning while the drawable's own Opacity applies like any other drawable's.
            using (context.PushBlendMode(BlendMode))
            using (context.PushTransform(transform))
            using (context.PushOpacity(Opacity / 100f))
            using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
            {
                context.DrawBackdrop(backdrop);
            }
        }
    }
}
