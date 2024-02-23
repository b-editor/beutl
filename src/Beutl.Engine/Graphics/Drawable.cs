using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics;

[DummyType(typeof(DummyDrawable))]
public abstract class Drawable : Renderable
{
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<FilterEffect?> FilterEffectProperty;
    public static readonly CoreProperty<AlignmentX> AlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> AlignmentYProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;
    public static readonly CoreProperty<IBrush?> FillProperty;
    public static readonly CoreProperty<IBrush?> OpacityMaskProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    private ITransform? _transform;
    private FilterEffect? _filterEffect;
    private AlignmentX _alignX = AlignmentX.Center;
    private AlignmentY _alignY = AlignmentY.Center;
    private RelativePoint _transformOrigin = RelativePoint.Center;
    private IBrush? _fill = null;
    private IBrush? _opacityMask;
    private BlendMode _blendMode = BlendMode.SrcOver;

    static Drawable()
    {
        TransformProperty = ConfigureProperty<ITransform?, Drawable>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .DefaultValue(null)
            .Register();

        FilterEffectProperty = ConfigureProperty<FilterEffect?, Drawable>(nameof(FilterEffect))
            .Accessor(o => o.FilterEffect, (o, v) => o.FilterEffect = v)
            .DefaultValue(null)
            .Register();

        AlignmentXProperty = ConfigureProperty<AlignmentX, Drawable>(nameof(AlignmentX))
            .Accessor(o => o.AlignmentX, (o, v) => o.AlignmentX = v)
            .DefaultValue(AlignmentX.Center)
            .Register();

        AlignmentYProperty = ConfigureProperty<AlignmentY, Drawable>(nameof(AlignmentY))
            .Accessor(o => o.AlignmentY, (o, v) => o.AlignmentY = v)
            .DefaultValue(AlignmentY.Center)
            .Register();

        TransformOriginProperty = ConfigureProperty<RelativePoint, Drawable>(nameof(TransformOrigin))
            .Accessor(o => o.TransformOrigin, (o, v) => o.TransformOrigin = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        FillProperty = ConfigureProperty<IBrush?, Drawable>(nameof(Fill))
            .Accessor(o => o.Fill, (o, v) => o.Fill = v)
            .Register();

        OpacityMaskProperty = ConfigureProperty<IBrush?, Drawable>(nameof(OpacityMask))
            .Accessor(o => o.OpacityMask, (o, v) => o.OpacityMask = v)
            .DefaultValue(null)
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, Drawable>(nameof(BlendMode))
            .Accessor(o => o.BlendMode, (o, v) => o.BlendMode = v)
            .DefaultValue(BlendMode.SrcOver)
            .Register();

        AffectsRender<Drawable>(
            TransformProperty, FilterEffectProperty,
            AlignmentXProperty, AlignmentYProperty,
            TransformOriginProperty,
            FillProperty, OpacityMaskProperty,
            BlendModeProperty);
    }

    // DrawableBrushで使われる
    public Rect Bounds { get; protected set; }

    [Display(Name = nameof(Strings.ImageFilter), ResourceType = typeof(Strings), GroupName = nameof(Strings.ImageFilter))]
    public FilterEffect? FilterEffect
    {
        get => _filterEffect;
        set => SetAndRaise(FilterEffectProperty, ref _filterEffect, value);
    }

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    [Display(Name = nameof(Strings.AlignmentX), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public AlignmentX AlignmentX
    {
        get => _alignX;
        set => SetAndRaise(AlignmentXProperty, ref _alignX, value);
    }

    [Display(Name = nameof(Strings.AlignmentY), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public AlignmentY AlignmentY
    {
        get => _alignY;
        set => SetAndRaise(AlignmentYProperty, ref _alignY, value);
    }

    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public RelativePoint TransformOrigin
    {
        get => _transformOrigin;
        set => SetAndRaise(TransformOriginProperty, ref _transformOrigin, value);
    }

    [Display(Name = nameof(Strings.Fill), ResourceType = typeof(Strings), GroupName = nameof(Strings.Fill))]
    public IBrush? Fill
    {
        get => _fill;
        set => SetAndRaise(FillProperty, ref _fill, value);
    }

    [Display(Name = nameof(Strings.OpacityMask), ResourceType = typeof(Strings))]
    public IBrush? OpacityMask
    {
        get => _opacityMask;
        set => SetAndRaise(OpacityMaskProperty, ref _opacityMask, value);
    }

    [Display(Name = nameof(Strings.BlendMode), ResourceType = typeof(Strings))]
    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetAndRaise(BlendModeProperty, ref _blendMode, value);
    }

    public virtual void Measure(Size availableSize)
    {
        Size size = MeasureCore(availableSize);
        Matrix transform = GetTransformMatrix(availableSize, size);
        var rect = new Rect(size);

        if (_filterEffect != null)
        {
            rect = _filterEffect.TransformBounds(rect);
        }

        Bounds = rect.TransformToAABB(transform);
    }

    protected abstract Size MeasureCore(Size availableSize);

    private Matrix GetTransformMatrix(Size availableSize, Size coreBounds)
    {
        Vector pt = CalculateTranslate(coreBounds, availableSize);
        Vector origin = TransformOrigin.ToPixels(coreBounds);
        Matrix offset = Matrix.CreateTranslation(origin);

        if (Transform is { IsEnabled: true })
        {
            return (-offset) * Transform.Value * offset * Matrix.CreateTranslation(pt);
        }
        else
        {
            return Matrix.CreateTranslation(pt);
        }
    }

    public virtual void Render(ICanvas canvas)
    {
        if (IsVisible)
        {
            Size availableSize = canvas.Size.ToSize(1);
            Size size = MeasureCore(availableSize);
            var rect = new Rect(size);
            if (_filterEffect != null)
            {
                rect = _filterEffect.TransformBounds(rect);
            }

            Matrix transform = GetTransformMatrix(availableSize, size);
            Rect transformedBounds = rect.TransformToAABB(transform);
            using (canvas.PushBlendMode(BlendMode))
            using (canvas.PushTransform(transform))
            using (_filterEffect == null ? new() : canvas.PushFilterEffect(_filterEffect))
            using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(size)))
            {
                OnDraw(canvas);
            }

            Bounds = transformedBounds;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Transform as Animatable)?.ApplyAnimations(clock);
        (FilterEffect as Animatable)?.ApplyAnimations(clock);
        (Fill as Animatable)?.ApplyAnimations(clock);
        (OpacityMask as Animatable)?.ApplyAnimations(clock);
    }

    protected abstract void OnDraw(ICanvas canvas);

    private Point CalculateTranslate(Size bounds, Size canvasSize)
    {
        float x = 0;
        float y = 0;

        if (float.IsFinite(canvasSize.Width))
        {
            x = -bounds.Width / 2;
            switch (AlignmentX)
            {
                case AlignmentX.Center:
                    x += canvasSize.Width / 2;
                    break;
                case AlignmentX.Right:
                    x += canvasSize.Width;
                    break;
            }
        }

        if (float.IsFinite(canvasSize.Height))
        {
            y = -bounds.Height / 2;
            switch (AlignmentY)
            {
                case AlignmentY.Center:
                    y += canvasSize.Height / 2;
                    break;
                case AlignmentY.Bottom:
                    y += canvasSize.Height;
                    break;
            }
        }

        return new Point(x, y);
    }
}
