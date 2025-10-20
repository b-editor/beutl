using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics;

[DummyType(typeof(DummyDrawable))]
public abstract partial class Drawable : EngineObject
{
    [Display(Name = nameof(Strings.ImageFilter), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.ImageFilter))]
    public IProperty<FilterEffect?> FilterEffect { get; } = Property.Create<FilterEffect?>();

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();

    [Display(Name = nameof(Strings.AlignmentX), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public IProperty<AlignmentX> AlignmentX { get; } = Property.CreateAnimatable(Media.AlignmentX.Center);

    [Display(Name = nameof(Strings.AlignmentY), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public IProperty<AlignmentY> AlignmentY { get; } = Property.CreateAnimatable(Media.AlignmentY.Center);

    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Transform))]
    public IProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(Strings.Fill), ResourceType = typeof(Strings), GroupName = nameof(Strings.Fill))]
    public IProperty<Brush?> Fill { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(Strings.BlendMode), ResourceType = typeof(Strings))]
    public IProperty<BlendMode> BlendMode { get; } = Property.CreateAnimatable(Graphics.BlendMode.SrcOver);

    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    protected abstract Size MeasureCore(Size availableSize, Resource resource);

    internal Size MeasureInternal(Size availableSize, Resource resource) => MeasureCore(availableSize, resource);

    internal Matrix GetTransformMatrix(Size availableSize, Size coreBounds, Resource resource)
    {
        Vector pt = CalculateTranslate(coreBounds, availableSize, resource);
        var origin = resource.TransformOrigin.ToPixels(coreBounds);
        Matrix offset = Matrix.CreateTranslation(origin);

        if (resource.Transform != null)
        {
            Matrix transform = resource.Transform.Matrix;
            return (-offset) * transform * offset * Matrix.CreateTranslation(pt);
        }
        else
        {
            return Matrix.CreateTranslation(pt);
        }
    }

    public virtual void Render(GraphicsContext2D context, Resource resource)
    {
        if (IsEnabled)
        {
            Size availableSize = context.Size.ToSize(1);
            Size size = MeasureCore(availableSize, resource);

            Matrix transform = GetTransformMatrix(availableSize, size, resource);
            using (context.PushBlendMode(resource.BlendMode))
            using (context.PushTransform(transform))
            using (context.PushOpacity(resource.Opacity / 100f))
            using (resource.FilterEffect == null ? new() : context.PushFilterEffect(resource.FilterEffect))
            {
                OnDraw(context, resource);
            }
        }
    }

    protected abstract void OnDraw(GraphicsContext2D context, Resource resource);

    private Point CalculateTranslate(Size bounds, Size canvasSize, Resource resource)
    {
        float x = 0;
        float y = 0;

        if (float.IsFinite(canvasSize.Width))
        {
            switch (resource.AlignmentX)
            {
                case Media.AlignmentX.Left:
                    x = 0;
                    break;
                case Media.AlignmentX.Center:
                    x = canvasSize.Width / 2 - bounds.Width / 2;
                    break;
                case Media.AlignmentX.Right:
                    x = canvasSize.Width - bounds.Width;
                    break;
            }
        }

        if (float.IsFinite(canvasSize.Height))
        {
            switch (resource.AlignmentY)
            {
                case Media.AlignmentY.Top:
                    y = 0;
                    break;
                case Media.AlignmentY.Center:
                    y = canvasSize.Height / 2 - bounds.Height / 2;
                    break;
                case Media.AlignmentY.Bottom:
                    y = canvasSize.Height - bounds.Height;
                    break;
            }
        }

        return new Point(x, y);
    }
}
