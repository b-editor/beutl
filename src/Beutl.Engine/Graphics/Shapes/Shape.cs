using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public abstract class Shape : Drawable
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    public static readonly CoreProperty<Stretch> StretchProperty;
    public static readonly CoreProperty<IPen?> PenProperty;
    public static readonly CoreProperty<Geometry?> CreatedGeometryProperty;
    private float _width = -1;
    private float _height = -1;
    private Stretch _stretch = Stretch.None;
    private IPen? _pen = null;
    private Geometry? _createdGeometry;

    static Shape()
    {
        WidthProperty = ConfigureProperty<float, Shape>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .DefaultValue(float.PositiveInfinity)
            .Register();

        HeightProperty = ConfigureProperty<float, Shape>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(float.PositiveInfinity)
            .Register();

        StretchProperty = ConfigureProperty<Stretch, Shape>(nameof(Stretch))
            .Accessor(o => o.Stretch, (o, v) => o.Stretch = v)
            .Register();

        PenProperty = ConfigureProperty<IPen?, Shape>(nameof(Pen))
            .Accessor(o => o.Pen, (o, v) => o.Pen = v)
            .Register();

        CreatedGeometryProperty = ConfigureProperty<Geometry?, Shape>(nameof(CreatedGeometry))
            .Accessor(o => o.CreatedGeometry, (o, v) => o.CreatedGeometry = v)
            .Register();

        AffectsRender<Shape>(
            WidthProperty, HeightProperty,
            StretchProperty, PenProperty, CreatedGeometryProperty);
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

    [Display(Name = nameof(Strings.Stroke), GroupName = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IPen? Pen
    {
        get => _pen;
        set => SetAndRaise(PenProperty, ref _pen, value);
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

        return CreatedGeometry;
    }

    public void InvalidateGeometry()
    {
        CreatedGeometry = null;
        RaiseInvalidated(new RenderInvalidatedEventArgs(this, nameof(CreatedGeometry)));
    }

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
            case Stretch.Uniform:
                sx = sy = Math.Min(sx, sy);
                break;
            case Stretch.UniformToFill:
                sx = sy = Math.Max(sx, sy);
                break;
            case Stretch.Fill:
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

    protected override Size MeasureCore(Size availableSize)
    {
        Geometry? geometry = GetOrCreateGeometry();
        if (geometry == null)
        {
            return default;
        }

        Vector scale = CalculateScale(new Size(Width, Height), geometry.Bounds, Stretch);
        Size size = geometry.Bounds.Size * scale;
        if (Pen != null)
        {
            size = size.Inflate(ActualThickness(Pen));
        }

        return size;
    }

    private static float ActualThickness(IPen pen)
    {
        return PenHelper.GetRealThickness(pen.StrokeAlignment, pen.Thickness);
    }

    protected abstract Geometry? CreateGeometry();

    protected override void OnDraw(ICanvas canvas)
    {
        Geometry? geometry = GetOrCreateGeometry();
        if (geometry == null)
            return;

        var requestedSize = new Size(Width, Height);
        Rect shapeBounds = geometry.Bounds;
        Vector scale = CalculateScale(requestedSize, shapeBounds, Stretch);
        Matrix matrix = Matrix.Identity;
        //Matrix matrix = Matrix.CreateTranslation(-shapeBounds.Position);

        if (Pen != null)
        {
            float thickness = ActualThickness(Pen);

            matrix *= Matrix.CreateTranslation(thickness, thickness);
        }

        matrix *= Matrix.CreateScale(scale);

        using (canvas.PushTransform(matrix))
        {
            canvas.DrawGeometry(geometry, Fill, Pen);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Pen as IAnimatable)?.ApplyAnimations(clock);
    }
}
