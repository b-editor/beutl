using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

// Drawable継承しているが、Drawableのメソッドは使っていない
[Display(Name = nameof(Strings.Decorator), ResourceType = typeof(Strings))]
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
            var boundsMemory = context.UseMemory<Rect>();
            var transformParams = (r.Transform, r.TransformOrigin, availableSize, boundsMemory);

            foreach (var child in r.Children)
            {
                using (context.PushBlendMode(r.BlendMode))
                using (context.PushNode(
                           transformParams,
                           b => new DrawableGroup.CustomTransformRenderNode(
                               b.Transform, b.TransformOrigin, b.availableSize,
                               Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory),
                           (n, b) => n.Update(
                               b.Transform, b.TransformOrigin, b.availableSize,
                               Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory)))
                using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
                using (context.PushNode(
                           boundsMemory,
                           b => new DrawableGroup.BoundsObserveNode(b),
                           (n, b) => n.Update(b)))
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
