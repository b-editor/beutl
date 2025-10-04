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
public abstract class Drawable : EngineObject
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
    public IProperty<IBrush?> Fill { get; } = Property.Create<IBrush?>();

    [Display(Name = nameof(Strings.BlendMode), ResourceType = typeof(Strings))]
    public IProperty<BlendMode> BlendMode { get; } = Property.CreateAnimatable(Graphics.BlendMode.SrcOver);

    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    protected abstract Size MeasureCore(Size availableSize);

    internal Matrix GetTransformMatrix(Size availableSize, Size coreBounds, GraphicsContext2D context)
    {
        var (transformOrigin, transform) = (context.Get(TransformOrigin, Transform));
        Vector pt = CalculateTranslate(coreBounds, availableSize, context);
        var origin = transformOrigin.ToPixels(coreBounds);
        Matrix offset = Matrix.CreateTranslation(origin);

        if (transform is { IsEnabled: true })
        {
            return (-offset) * transform.Value * offset * Matrix.CreateTranslation(pt);
        }
        else
        {
            return Matrix.CreateTranslation(pt);
        }
    }

    public virtual void Render(GraphicsContext2D context)
    {
        if (IsEnabled)
        {
            Size availableSize = context.Size.ToSize(1);
            Size size = MeasureCore(availableSize);

            Matrix transform = GetTransformMatrix(availableSize, size, context);
            using (context.PushBlendMode(context.Get(BlendMode)))
            using (context.PushTransform(transform))
            using (context.PushOpacity(context.Get(Opacity) / 100f))
            using (FilterEffect.CurrentValue == null ? new() : context.PushFilterEffect(FilterEffect.CurrentValue))
            {
                OnDraw(context);
            }
        }
    }

    protected abstract void OnDraw(GraphicsContext2D context);

    private Point CalculateTranslate(Size bounds, Size canvasSize, GraphicsContext2D context)
    {
        float x = 0;
        float y = 0;

        if (float.IsFinite(canvasSize.Width))
        {
            switch (context.Get(AlignmentX))
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
            switch (context.Get(AlignmentY))
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
