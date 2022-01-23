namespace BeUtl.Graphics;

public sealed class Ellipse : Drawable
{
    public static readonly CoreProperty<float> StrokeWidthProperty;
    private float _strokeWidth;

    static Ellipse()
    {
        StrokeWidthProperty = ConfigureProperty<float, Ellipse>(nameof(StrokeWidth))
            .Accessor(o => o.StrokeWidth, (o, v) => o.StrokeWidth = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(4000)
            .Register();

        AffectsRender<Ellipse>(StrokeWidthProperty);
    }

    public float StrokeWidth
    {
        get => _strokeWidth;
        set => SetAndRaise(StrokeWidthProperty, ref _strokeWidth, value);
    }

    public override void Dispose()
    {
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return new Size(Math.Max(Width, 0), Math.Max(Height, 0));
    }

    protected override void OnDraw(ICanvas canvas)
    {
        canvas.StrokeWidth = StrokeWidth;
        if (Width > 0 && Height > 0)
        {
            canvas.DrawCircle(new Size(Width, Height));
        }
    }
}
