using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public abstract partial class Shape : Drawable
{
    public Shape()
    {
        ScanProperties<Shape>();
        Fill.CurrentValue = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(Strings.Stroke), GroupName = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(Strings.Fill), ResourceType = typeof(Strings), GroupName = nameof(Strings.Fill))]
    public IProperty<Brush?> Fill { get; } = Property.Create<Brush?>();

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        Geometry.Resource? geometry = r.GetGeometry();
        if (geometry == null)
        {
            return default;
        }

        Size size = geometry.Bounds.Size;
        if (r.Pen != null)
        {
            size = size.Inflate(ActualThickness(r.Pen));
        }

        return size;
    }

    private static float ActualThickness(Pen.Resource pen)
    {
        return PenHelper.GetRealThickness(pen.StrokeAlignment, pen.Thickness);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        Geometry.Resource? geometry = r.GetGeometry();
        if (geometry == null)
            return;

        Matrix matrix = Matrix.Identity;
        //Matrix matrix = Matrix.CreateTranslation(-shapeBounds.Position);

        if (r.Pen != null)
        {
            float thickness = ActualThickness(r.Pen);

            matrix *= Matrix.CreateTranslation(thickness, thickness);
        }

        using (context.PushTransform(matrix))
        {
            context.DrawGeometry(geometry, r.Fill, r.Pen);
        }
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.IsEnabled)
        {
            Size availableSize = context.Size.ToSize(1);
            Size size = MeasureCore(availableSize, resource);

            Matrix transform = GetTransformMatrix(availableSize, size, resource);
            using (context.PushBlendMode(r.BlendMode))
            using (context.PushTransform(transform))
            using (context.PushOpacity(r.Opacity / 100f))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            {
                OnDraw(context, resource);
            }
        }
    }

    public abstract partial class Resource
    {
        public abstract Geometry.Resource? GetGeometry();
    }
}
