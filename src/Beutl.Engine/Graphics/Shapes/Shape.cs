using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public abstract partial class Shape : Drawable
{
    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>(-1);

    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>(-1);

    public IProperty<Stretch> Stretch { get; } = Property.CreateAnimatable(Media.Stretch.None);

    [Display(Name = nameof(Strings.Stroke), GroupName = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    internal static Vector CalculateScale(Size requestedSize, Rect shapeBounds, Stretch stretch)
    {
        var shapeSize = shapeBounds.Size;
        float desiredX = requestedSize.Width;
        float desiredY = requestedSize.Height;
        bool widthInfinityOrNegative = float.IsInfinity(requestedSize.Width) || requestedSize.Width < 0;
        bool heightInfinityOrNegative = float.IsInfinity(requestedSize.Height) || requestedSize.Height < 0;

        float sx = 0.0f;
        float sy = 0.0f;

        if (widthInfinityOrNegative)
        {
            desiredX = shapeSize.Width;
        }

        if (heightInfinityOrNegative)
        {
            desiredY = shapeSize.Height;
        }

        if (shapeBounds.Width > 0)
        {
            sx = desiredX / shapeSize.Width;
        }

        if (shapeBounds.Height > 0)
        {
            sy = desiredY / shapeSize.Height;
        }

        if (widthInfinityOrNegative)
        {
            sx = sy;
        }

        if (heightInfinityOrNegative)
        {
            sy = sx;
        }

        switch (stretch)
        {
            case Media.Stretch.Uniform:
                sx = sy = Math.Min(sx, sy);
                break;
            case Media.Stretch.UniformToFill:
                sx = sy = Math.Max(sx, sy);
                break;
            case Media.Stretch.Fill:
                if (widthInfinityOrNegative)
                {
                    sx = 1.0f;
                }

                if (heightInfinityOrNegative)
                {
                    sy = 1.0f;
                }

                break;
            default:
                sx = sy = 1;
                break;
        }

        return new Vector(sx, sy);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        Geometry.Resource? geometry = r.GetGeometry();
        if (geometry == null)
        {
            return default;
        }

        Vector scale = CalculateScale(new Size(r.Width, r.Height), geometry.Bounds, r.Stretch);
        Size size = geometry.Bounds.Size * scale;
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

        var requestedSize = new Size(r.Width, r.Height);
        Rect shapeBounds = geometry.Bounds;
        Vector scale = CalculateScale(requestedSize, shapeBounds, r.Stretch);
        Matrix matrix = Matrix.Identity;
        //Matrix matrix = Matrix.CreateTranslation(-shapeBounds.Position);

        if (r.Pen != null)
        {
            float thickness = ActualThickness(r.Pen);

            matrix *= Matrix.CreateTranslation(thickness, thickness);
        }

        matrix *= Matrix.CreateScale(scale);

        using (context.PushTransform(matrix))
        {
            context.DrawGeometry(geometry, r.Fill, r.Pen);
        }
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (IsEnabled)
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
