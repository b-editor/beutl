using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Filters;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Rendering;

namespace Beutl.Graphics;

public abstract class Drawable : Renderable, IDrawable, IHierarchical
{
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<IImageFilter?> FilterProperty;
    public static readonly CoreProperty<IBitmapEffect?> EffectProperty;
    public static readonly CoreProperty<AlignmentX> AlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> AlignmentYProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;
    public static readonly CoreProperty<IBrush?> ForegroundProperty;
    public static readonly CoreProperty<IBrush?> OpacityMaskProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    private ITransform? _transform;
    private IImageFilter? _filter;
    private IBitmapEffect? _effect;
    private AlignmentX _alignX;
    private AlignmentY _alignY;
    private RelativePoint _transformOrigin;
    private IBrush? _foreground;
    private IBrush? _opacityMask;
    private BlendMode _blendMode = BlendMode.SrcOver;

    static Drawable()
    {
        TransformProperty = ConfigureProperty<ITransform?, Drawable>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .DefaultValue(null)
            .Register();

        FilterProperty = ConfigureProperty<IImageFilter?, Drawable>(nameof(Filter))
            .Accessor(o => o.Filter, (o, v) => o.Filter = v)
            .DefaultValue(null)
            .Register();

        EffectProperty = ConfigureProperty<IBitmapEffect?, Drawable>(nameof(Effect))
            .Accessor(o => o.Effect, (o, v) => o.Effect = v)
            .DefaultValue(null)
            .Register();

        AlignmentXProperty = ConfigureProperty<AlignmentX, Drawable>(nameof(AlignmentX))
            .Accessor(o => o.AlignmentX, (o, v) => o.AlignmentX = v)
            .DefaultValue(AlignmentX.Left)
            .Register();

        AlignmentYProperty = ConfigureProperty<AlignmentY, Drawable>(nameof(AlignmentY))
            .Accessor(o => o.AlignmentY, (o, v) => o.AlignmentY = v)
            .DefaultValue(AlignmentY.Top)
            .Register();

        TransformOriginProperty = ConfigureProperty<RelativePoint, Drawable>(nameof(TransformOrigin))
            .Accessor(o => o.TransformOrigin, (o, v) => o.TransformOrigin = v)
            .Register();

        ForegroundProperty = ConfigureProperty<IBrush?, Drawable>(nameof(Foreground))
            .Accessor(o => o.Foreground, (o, v) => o.Foreground = v)
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
            TransformProperty, FilterProperty, EffectProperty,
            AlignmentXProperty, AlignmentYProperty,
            TransformOriginProperty,
            ForegroundProperty, OpacityMaskProperty,
            BlendModeProperty);
    }

    public Rect Bounds { get; private set; }

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    [Display(Name = nameof(Strings.ImageFilter), ResourceType = typeof(Strings))]
    public IImageFilter? Filter
    {
        get => _filter;
        set => SetAndRaise(FilterProperty, ref _filter, value);
    }

    [Display(Name = nameof(Strings.BitmapEffect), ResourceType = typeof(Strings))]
    public IBitmapEffect? Effect
    {
        get => _effect;
        set => SetAndRaise(EffectProperty, ref _effect, value);
    }

    [Display(Name = nameof(Strings.AlignmentX), ResourceType = typeof(Strings))]
    public AlignmentX AlignmentX
    {
        get => _alignX;
        set => SetAndRaise(AlignmentXProperty, ref _alignX, value);
    }

    [Display(Name = nameof(Strings.AlignmentY), ResourceType = typeof(Strings))]
    public AlignmentY AlignmentY
    {
        get => _alignY;
        set => SetAndRaise(AlignmentYProperty, ref _alignY, value);
    }

    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings))]
    public RelativePoint TransformOrigin
    {
        get => _transformOrigin;
        set => SetAndRaise(TransformOriginProperty, ref _transformOrigin, value);
    }

    [Display(Name = nameof(Strings.Foreground), ResourceType = typeof(Strings))]
    public IBrush? Foreground
    {
        get => _foreground;
        set => SetAndRaise(ForegroundProperty, ref _foreground, value);
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

    public IBitmap ToBitmap()
    {
        Size size = MeasureCore(Size.Infinity);
        int width = (int)size.Width;
        int height = (int)size.Height;
        if (width > 0 && height > 0)
        {
            using (var canvas = new Canvas(width, height))
            using (Foreground == null ? new() : canvas.PushFillBrush(Foreground))
            {
                OnDraw(canvas);
                return canvas.GetBitmap();
            }
        }
        else
        {
            return new Bitmap<Bgra8888>(0, 0);
        }
    }

    public void Measure(Size availableSize)
    {
        Size size = MeasureCore(availableSize);
        Matrix transform = GetTransformMatrix(availableSize, size);
        var rect = new Rect(size);

        if (_effect != null)
        {
            rect = _effect.TransformBounds(rect);
        }

        if (_filter != null)
        {
            rect = _filter.TransformBounds(rect);
        }

        Bounds = rect.TransformToAABB(transform);
    }

    protected abstract Size MeasureCore(Size availableSize);

    private Matrix GetTransformMatrix(Size availableSize, Size coreBounds)
    {
        Vector pt = CalculateTranslate(coreBounds);
        Vector origin = CalculateOriginPoint(availableSize);
        Matrix offset = Matrix.CreateTranslation(origin);

        if (Transform is { })
        {
            return Matrix.CreateTranslation(pt) * Transform.Value * offset;
        }
        else
        {
            return offset * Matrix.CreateTranslation(pt);
        }
    }

    private void HasBitmapEffect(ICanvas canvas)
    {
        Size availableSize = canvas.Size.ToSize(1);
        Size size = MeasureCore(availableSize);
        var rect = new Rect(size);
        Matrix transform = GetTransformMatrix(availableSize, size);
        Matrix transformFact = transform;
        if (_effect != null)
        {
            rect = _effect.TransformBounds(rect);
            transformFact = transform.Append(Matrix.CreateTranslation(rect.Position));
        }
        if (_filter != null)
        {
            rect = _filter.TransformBounds(rect);
        }

        rect = rect.TransformToAABB(transform);
        using (Foreground == null ? new() : canvas.PushFillBrush(Foreground))
        using (canvas.PushBlendMode(BlendMode))
        using (canvas.PushTransform(transformFact))
        using (_filter == null ? new() : canvas.PushImageFilter(_filter))
        using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, rect))
        {
            IBitmap bitmap = ToBitmap();
            if (bitmap is Bitmap<Bgra8888> bmp)
            {
                _effect!.Processor.Process(in bmp, out Bitmap<Bgra8888> outBmp);
                if (bmp != outBmp)
                {
                    bmp.Dispose();
                }

                //if (Width > 0 && Height > 0)
                //{
                //    using (canvas.PushTransform(Matrix.CreateScale(Width / outBmp.Width, Height / outBmp.Height)))
                //    {
                //        canvas.DrawBitmap(outBmp);
                //    }
                //}
                //else
                {
                    canvas.DrawBitmap(outBmp);
                }

                outBmp.Dispose();
            }
            else
            {
                bitmap.Dispose();
                OnDraw(canvas);
            }
        }

        Bounds = rect;
    }

    public void Draw(ICanvas canvas)
    {
        if (IsVisible)
        {
            if (_effect?.IsEnabled == true)
            {
                HasBitmapEffect(canvas);
            }
            else
            {
                Size availableSize = canvas.Size.ToSize(1);
                Size size = MeasureCore(availableSize);
                var rect = new Rect(size);
                if (_filter != null)
                {
                    rect = _filter.TransformBounds(rect);
                }

                Matrix transform = GetTransformMatrix(availableSize, size);

                using (Foreground == null ? new() : canvas.PushFillBrush(Foreground))
                using (canvas.PushBlendMode(BlendMode))
                using (canvas.PushTransform(transform))
                using (_filter == null ? new() : canvas.PushImageFilter(_filter))
                using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(size)))
                {
                    OnDraw(canvas);
                }

                Bounds = rect.TransformToAABB(transform);
            }
        }

#if DEBUG
        //Rect bounds = Bounds.Inflate(10);
        //using (canvas.PushTransform(Matrix.CreateTranslation(bounds.Position)))
        //using (canvas.PushStrokeWidth(5))
        //{
        //    canvas.DrawRect(bounds.Size);
        //}
#endif
    }

    public override void Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Transform as Animatable)?.ApplyAnimations(clock);
        (Filter as Animatable)?.ApplyAnimations(clock);
        (Effect as Animatable)?.ApplyAnimations(clock);
        (Foreground as Animatable)?.ApplyAnimations(clock);
        (OpacityMask as Animatable)?.ApplyAnimations(clock);
    }

    protected abstract void OnDraw(ICanvas canvas);

    private Point CalculateOriginPoint(Size size)
    {
        if (float.IsNormal(size.Width) && float.IsNormal(size.Height))
        {
            return TransformOrigin.ToPixels(size);
        }
        else if (TransformOrigin.Unit == RelativeUnit.Absolute)
        {
            return TransformOrigin.Point;
        }
        else
        {
            return default;
        }
    }

    private Point CalculateTranslate(Size bounds)
    {
        float x = 0;
        float y = 0;

        if (AlignmentX == AlignmentX.Center)
        {
            x -= bounds.Width / 2;
        }
        else if (AlignmentX == AlignmentX.Right)
        {
            x -= bounds.Width;
        }

        if (AlignmentY == AlignmentY.Center)
        {
            y -= bounds.Height / 2;
        }
        else if (AlignmentY == AlignmentY.Bottom)
        {
            y -= bounds.Height;
        }

        return new Point(x, y);
    }
}
