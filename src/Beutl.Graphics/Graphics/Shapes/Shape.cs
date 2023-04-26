using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public abstract class Shape : Drawable
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    public static readonly CoreProperty<Stretch> StretchProperty;
    public static readonly CoreProperty<IPen?> PenProperty;
    public static readonly CoreProperty<PathFillType> FillTypeProperty;
    public static readonly CoreProperty<Geometry?> CreatedGeometryProperty;
    private float _width = 0;
    private float _height = 0;
    private Stretch _stretch = Stretch.None;
    private IPen? _pen = null;
    private PathFillType _fillType;
    private Geometry? _createdGeometry;

    static Shape()
    {
        WidthProperty = ConfigureProperty<float, Shape>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .DefaultValue(0)
            .Register();

        HeightProperty = ConfigureProperty<float, Shape>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(0)
            .Register();

        StretchProperty = ConfigureProperty<Stretch, Shape>(nameof(Stretch))
            .Accessor(o => o.Stretch, (o, v) => o.Stretch = v)
            .Register();

        PenProperty = ConfigureProperty<IPen?, Shape>(nameof(Pen))
            .Accessor(o => o.Pen, (o, v) => o.Pen = v)
            .Register();

        FillTypeProperty = ConfigureProperty<PathFillType, Shape>(nameof(FillType))
            .Accessor(o => o.FillType, (o, v) => o.FillType = v)
            .Register();

        CreatedGeometryProperty = ConfigureProperty<Geometry?, Shape>(nameof(CreatedGeometry))
            .Accessor(o => o.CreatedGeometry, (o, v) => o.CreatedGeometry = v)
            .Register();

        AffectsRender<Shape>(
            WidthProperty, HeightProperty,
            StretchProperty, PenProperty, FillTypeProperty, CreatedGeometryProperty);
    }

    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public float Width
    {
        get => _width;
        set => SetAndRaise(WidthProperty, ref _width, value);
    }

    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public float Height
    {
        get => _height;
        set => SetAndRaise(HeightProperty, ref _height, value);
    }

    public Stretch Stretch
    {
        get => _stretch;
        set => SetAndRaise(StretchProperty, ref _stretch, value);
    }

    public IPen? Pen
    {
        get => _pen;
        set => SetAndRaise(PenProperty, ref _pen, value);
    }

    public PathFillType FillType
    {
        get => _fillType;
        set => SetAndRaise(FillTypeProperty, ref _fillType, value);
    }

    public Geometry? CreatedGeometry
    {
        get => _createdGeometry;
        private set => SetAndRaise(CreatedGeometryProperty, ref _createdGeometry, value);
    }

    protected static void AffectsGeometry<T>(params CoreProperty[] properties)
        where T : Shape
    {
        foreach (CoreProperty item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.InvalidateGeometry();

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                    {
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderGeometryInvalidated;
                    }

                    if (e.NewValue is IAffectsRender newAffectsRender)
                    {
                        newAffectsRender.Invalidated += s.OnAffectsRenderGeometryInvalidated;
                    }
                }
            });
        }
    }

    private void OnAffectsRenderGeometryInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        InvalidateGeometry();
    }

    private Geometry? GetOrCreateGeometry()
    {
        CreatedGeometry ??= CreateGeometry();
        if (CreatedGeometry != null)
        {
            CreatedGeometry.FillType = FillType;
        }

        return CreatedGeometry;
    }

    public void InvalidateGeometry()
    {
        CreatedGeometry = null;
    }

    private static (Size size, Matrix transform) CalculateSizeAndTransform(Size requestedSize, Rect shapeBounds, Stretch stretch)
    {
        var shapeSize = new Size(shapeBounds.Right, shapeBounds.Bottom);
        Matrix translate = Matrix.Identity;
        float desiredX = requestedSize.Width;
        float desiredY = requestedSize.Height;
        float sx = 0.0f;
        float sy = 0.0f;

        if (stretch != Stretch.None)
        {
            shapeSize = shapeBounds.Size;
            translate = Matrix.CreateTranslation(-(Vector)shapeBounds.Position);
        }

        if (float.IsInfinity(requestedSize.Width))
        {
            desiredX = shapeSize.Width;
        }

        if (float.IsInfinity(requestedSize.Height))
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

        if (float.IsInfinity(requestedSize.Width))
        {
            sx = sy;
        }

        if (float.IsInfinity(requestedSize.Height))
        {
            sy = sx;
        }

        switch (stretch)
        {
            case Stretch.Uniform:
                sx = sy = Math.Min(sx, sy);
                break;
            case Stretch.UniformToFill:
                sx = sy = Math.Max(sx, sy);
                break;
            case Stretch.Fill:
                if (float.IsInfinity(requestedSize.Width))
                {
                    sx = 1.0f;
                }

                if (float.IsInfinity(requestedSize.Height))
                {
                    sy = 1.0f;
                }

                break;
            default:
                sx = sy = 1;
                break;
        }

        Matrix transform = translate * Matrix.CreateScale(sx, sy);
        var size = new Size(shapeSize.Width * sx, shapeSize.Height * sy);
        return (size, transform);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        Geometry? geometry = GetOrCreateGeometry();
        if (geometry == null)
        {
            return default;
        }

        return CalculateSizeAndTransform(new Size(Width, Height), geometry.Bounds, Stretch).size;
    }

    protected abstract Geometry? CreateGeometry();

    protected override void OnDraw(ICanvas canvas)
    {
        Geometry? geometry = GetOrCreateGeometry();
        if (geometry == null)
            return;

        var requestedSize = new Size(Width, Height);
        (Size _, Matrix transform) = CalculateSizeAndTransform(requestedSize, geometry.Bounds, Stretch);

        using (canvas.PushTransform(transform))
        using (canvas.PushPen(Pen))
        {
            canvas.DrawGeometry(geometry);
        }
    }
}
