using BeUtl.Graphics.Filters;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.Rendering;
using BeUtl.Styling;

namespace BeUtl.Graphics;

public abstract class Drawable : Renderable, IDrawable, ILogicalElement
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<IImageFilter?> FilterProperty;
    public static readonly CoreProperty<AlignmentX> CanvasAlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> CanvasAlignmentYProperty;
    public static readonly CoreProperty<AlignmentX> AlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> AlignmentYProperty;
    public static readonly CoreProperty<IBrush?> ForegroundProperty;
    public static readonly CoreProperty<IBrush?> OpacityMaskProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    private float _width = -1;
    private float _height = -1;
    private ITransform? _transform;
    private IImageFilter? _filter;
    private AlignmentX _cAlignX;
    private AlignmentY _cAlignY;
    private AlignmentX _alignX;
    private AlignmentY _alignY;
    private IBrush? _foreground;
    private IBrush? _opacityMask;
    private BlendMode _blendMode = BlendMode.SrcOver;

    static Drawable()
    {
        WidthProperty = ConfigureProperty<float, Drawable>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(-1)
            .Register();

        HeightProperty = ConfigureProperty<float, Drawable>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(-1)
            .Register();

        TransformProperty = ConfigureProperty<ITransform?, Drawable>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(null)
            .Register();

        FilterProperty = ConfigureProperty<IImageFilter?, Drawable>(nameof(Filter))
            .Accessor(o => o.Filter, (o, v) => o.Filter = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(null)
            .Register();

        CanvasAlignmentXProperty = ConfigureProperty<AlignmentX, Drawable>(nameof(CanvasAlignmentX))
            .Accessor(o => o.CanvasAlignmentX, (o, v) => o.CanvasAlignmentX = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(AlignmentX.Left)
            .Register();

        CanvasAlignmentYProperty = ConfigureProperty<AlignmentY, Drawable>(nameof(CanvasAlignmentY))
            .Accessor(o => o.CanvasAlignmentY, (o, v) => o.CanvasAlignmentY = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(AlignmentY.Top)
            .Register();

        AlignmentXProperty = ConfigureProperty<AlignmentX, Drawable>(nameof(AlignmentX))
            .Accessor(o => o.AlignmentX, (o, v) => o.AlignmentX = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(AlignmentX.Left)
            .Register();

        AlignmentYProperty = ConfigureProperty<AlignmentY, Drawable>(nameof(AlignmentY))
            .Accessor(o => o.AlignmentY, (o, v) => o.AlignmentY = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(AlignmentY.Top)
            .Register();

        ForegroundProperty = ConfigureProperty<IBrush?, Drawable>(nameof(Foreground))
            .Accessor(o => o.Foreground, (o, v) => o.Foreground = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        OpacityMaskProperty = ConfigureProperty<IBrush?, Drawable>(nameof(OpacityMask))
            .Accessor(o => o.OpacityMask, (o, v) => o.OpacityMask = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(null)
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, Drawable>(nameof(BlendMode))
            .Accessor(o => o.BlendMode, (o, v) => o.BlendMode = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(BlendMode.SrcOver)
            .Register();

        AffectsRender<Drawable>(
            WidthProperty, HeightProperty,
            TransformProperty, FilterProperty,
            CanvasAlignmentXProperty, CanvasAlignmentYProperty,
            AlignmentXProperty, AlignmentYProperty,
            ForegroundProperty, OpacityMaskProperty,
            BlendModeProperty);
    }

    ~Drawable()
    {
        if (!IsDisposed)
        {
            Dispose();
        }
    }

    public float Width
    {
        get => _width;
        set => SetAndRaise(WidthProperty, ref _width, value);
    }

    public float Height
    {
        get => _height;
        set => SetAndRaise(HeightProperty, ref _height, value);
    }

    public Rect Bounds { get; private set; }

    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    public IImageFilter? Filter
    {
        get => _filter;
        set => SetAndRaise(FilterProperty, ref _filter, value);
    }

    public AlignmentX CanvasAlignmentX
    {
        get => _cAlignX;
        set => SetAndRaise(CanvasAlignmentXProperty, ref _cAlignX, value);
    }

    public AlignmentY CanvasAlignmentY
    {
        get => _cAlignY;
        set => SetAndRaise(CanvasAlignmentYProperty, ref _cAlignY, value);
    }

    public AlignmentX AlignmentX
    {
        get => _alignX;
        set => SetAndRaise(AlignmentXProperty, ref _alignX, value);
    }

    public AlignmentY AlignmentY
    {
        get => _alignY;
        set => SetAndRaise(AlignmentYProperty, ref _alignY, value);
    }

    public IBrush? Foreground
    {
        get => _foreground;
        set => SetAndRaise(ForegroundProperty, ref _foreground, value);
    }

    public IBrush? OpacityMask
    {
        get => _opacityMask;
        set => SetAndRaise(OpacityMaskProperty, ref _opacityMask, value);
    }

    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetAndRaise(BlendModeProperty, ref _blendMode, value);
    }

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren
    {
        get
        {
            if (Transform is ILogicalElement log)
            {
                yield return log;
            }

            if (Filter is ILogicalElement log2)
            {
                yield return log2;
            }
        }
    }

    public void Initialize()
    {
        OnInitialize();
        CanvasAlignmentX = AlignmentX.Left;
        CanvasAlignmentY = AlignmentY.Top;
        AlignmentX = AlignmentX.Left;
        AlignmentY = AlignmentY.Top;
        Foreground = Colors.White.ToBrush();
        OpacityMask = null;
        BlendMode = BlendMode.SrcOver;
        Transform = null;
        Filter = null;
    }

    public IBitmap ToBitmap()
    {
        VerifyAccess();
        Size size = MeasureCore(Size.Infinity);
        using var canvas = new Canvas((int)size.Width, (int)size.Height);

        OnDraw(canvas);

        return canvas.GetBitmap();
    }

    public void Measure(Size availableSize)
    {
        Size size = MeasureCore(availableSize);
        Vector pt = CreatePoint(availableSize);
        Vector relpt = CreateRelPoint(size);
        Matrix transform = Transform?.Value == null ?
            Matrix.CreateTranslation(relpt) * Matrix.CreateTranslation(pt) :
            Matrix.CreateTranslation(relpt) * Transform.Value * Matrix.CreateTranslation(pt);
        var rect = new Rect(size);

        Bounds = (Filter?.TransformBounds(rect) ?? rect).TransformToAABB(transform);
    }

    protected abstract Size MeasureCore(Size availableSize);

    public void Draw(ICanvas canvas)
    {
        VerifyAccess();
        if (IsVisible)
        {
            Size availableSize = canvas.Size.ToSize(1);
            Size size = MeasureCore(availableSize);
            Vector pt = CreatePoint(availableSize);
            Vector relpt = CreateRelPoint(size);
            Matrix transform = Transform?.Value == null ?
                Matrix.CreateTranslation(relpt) * Matrix.CreateTranslation(pt) :
                Matrix.CreateTranslation(relpt) * Transform.Value * Matrix.CreateTranslation(pt);
            var rect = new Rect(size);

            using (Foreground == null ? new() : canvas.PushForeground(Foreground))
            using (canvas.PushBlendMode(BlendMode))
            using (canvas.PushTransform(transform))
            using (Filter == null ? new() : canvas.PushFilters(Filter))
            using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(size)))
            {
                OnDraw(canvas);
            }

            Bounds = (Filter?.TransformBounds(rect) ?? rect).TransformToAABB(transform);
        }

        IsDirty = false;
#if DEBUG
        //Rect bounds = Bounds;
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

    protected abstract void OnDraw(ICanvas canvas);

    protected virtual void OnInitialize()
    {
    }

    private Point CreateRelPoint(Size size)
    {
        float x = 0;
        float y = 0;

        if (AlignmentX == AlignmentX.Center)
        {
            x -= size.Width / 2;
        }
        else if (AlignmentX == AlignmentX.Right)
        {
            x -= size.Width;
        }

        if (AlignmentY == AlignmentY.Center)
        {
            y -= size.Height / 2;
        }
        else if (AlignmentY == AlignmentY.Bottom)
        {
            y -= size.Height;
        }

        return new Point(x, y);
    }

    private Point CreatePoint(Size canvasSize)
    {
        float x = 0;
        float y = 0;

        if (CanvasAlignmentX == AlignmentX.Center)
        {
            x += canvasSize.Width / 2;
        }
        else if (CanvasAlignmentX == AlignmentX.Right)
        {
            x += canvasSize.Width;
        }

        if (CanvasAlignmentY == AlignmentY.Center)
        {
            y += canvasSize.Height / 2;
        }
        else if (CanvasAlignmentY == AlignmentY.Bottom)
        {
            y += canvasSize.Height;
        }

        return new Point(x, y);
    }
}
