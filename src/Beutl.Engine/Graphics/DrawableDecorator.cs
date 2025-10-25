using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

// Drawable継承しているが、Drawableのメソッドは使っていない
[Display(Name = "Decorator")]
public sealed partial class DrawableDecorator : Drawable
{
    public DrawableDecorator()
    {
        ScanProperties<DrawableDecorator>();
    }

    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (resource.IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size.ToSize(1);

            foreach (var child in r.Children)
            {
                using (context.PushBlendMode(r.BlendMode))
                // NOTE: TransformOriginはGroupのFilterEffect適用後のBoundsに基づいて計算される、通常のTransformとは異なるため注意
                using (context.PushBoundaryTransform(r.Transform, r.TransformOrigin, availableSize, Media.AlignmentX.Left, Media.AlignmentY.Top))
                using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))  // TODO: ここでのCalculateBoundsをPushTransformまで持っていきたい
                {
                    context.DrawDrawable(child);
                }
            }
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return Size.Empty;
    }
}
