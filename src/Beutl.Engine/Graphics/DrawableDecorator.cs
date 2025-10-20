using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

// TODO: トランスフォームの動作が変わるので検証する
//       Child(エフェクト適用後)のBoundsからトランスフォームしていたのが、
//       エフェクト適用前のBoundsからトランスフォームするようになる
// Drawable継承しているが、Drawableのメソッドは使っていない
[Display(Name = "Decorator")]
public sealed partial class DrawableDecorator : Drawable
{
    public DrawableDecorator()
    {
        ScanProperties<DrawableDecorator>();
    }

    public IProperty<Drawable?> Child { get; } = Property.Create<Drawable?>();

    public int OriginalZIndex => (Child.CurrentValue as DrawableDecorator)?.OriginalZIndex ?? ZIndex;

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size.ToSize(1);

            Matrix transform = GetTransformMatrix(availableSize, r);
            using (context.PushBlendMode(r.BlendMode))
            using (context.PushTransform(transform))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            {
                OnDraw(context, resource);
            }
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Child != null)
        {
            context.DrawDrawable(r.Child);
        }
    }

    private Matrix GetTransformMatrix(Size availableSize, Drawable.Resource resource)
    {
        Vector origin = resource.TransformOrigin.ToPixels(availableSize);
        Matrix offset = Matrix.CreateTranslation(origin);
        var transform = resource.Transform;

        if (transform != null)
        {
            return (-offset) * transform.Matrix * offset;
        }
        else
        {
            return Matrix.Identity;
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Child != null)
        {
            return r.Child.GetOriginal().MeasureInternal(availableSize, r.Child);
        }
        else
        {
            return Size.Empty;
        }
    }
}
