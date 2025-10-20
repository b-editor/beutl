using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

[Display(Name = "Group")]
public sealed partial class DrawableGroup : Drawable
{
    public DrawableGroup()
    {
        Children = new Drawables(this);
        Children.Invalidated += (_, e) => RaiseInvalidated(e);
        ScanProperties<DrawableGroup>();
    }

    public Drawables Children { get; }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size.ToSize(1);
            Matrix transform = GetTransformMatrix(availableSize, r);

            using (context.PushBlendMode(r.BlendMode))
            using (context.PushLayer())
            using (context.PushTransform(transform))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            using (context.PushLayer())
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

    private Matrix GetTransformMatrix(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        Vector origin = r.TransformOrigin.ToPixels(availableSize);
        Matrix offset = Matrix.CreateTranslation(origin);
        var transform = r.Transform;

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
        return Size.Empty;
    }
}
