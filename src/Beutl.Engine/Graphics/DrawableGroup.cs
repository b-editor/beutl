using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

[Display(Name = "Group")]
public sealed partial class DrawableGroup : Drawable
{
    public DrawableGroup()
    {
        ScanProperties<DrawableGroup>();
    }

    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    // TODO: これと同じことをするFilterEffectを作る
    public IProperty<bool> Concat { get; } = Property.Create(false);

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (resource.IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size.ToSize(1);

            using (context.PushBlendMode(r.BlendMode))
            // NOTE: TransformOriginはGroupのFilterEffect適用後のBoundsに基づいて計算される、通常のTransformとは異なるため注意
            using (r.Concat
                ? context.PushBoundaryTransform(r.Transform, r.TransformOrigin, availableSize, Media.AlignmentX.Left, Media.AlignmentY.Top)
                : context.PushSplittedTransform(r.Transform, r.TransformOrigin, availableSize, Media.AlignmentX.Left, Media.AlignmentY.Top))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            using (r.Concat ? context.PushLayer() : new()) // TODO: ここでのCalculateBoundsをPushTransformまで持っていきたい
            {
                OnDraw(context, r);
            }
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        foreach (Drawable.Resource item in r.Children)
        {
            context.DrawDrawable(item);
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return Size.Empty;
    }
}
