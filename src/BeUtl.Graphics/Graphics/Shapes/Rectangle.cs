namespace Beutl.Graphics.Shapes;

public sealed class Rectangle : Drawable
{
    public static readonly CoreProperty<float> StrokeWidthProperty;
    private float _strokeWidth = 4000;

    static Rectangle()
    {
        StrokeWidthProperty = ConfigureProperty<float, Rectangle>(nameof(StrokeWidth))
            .Accessor(o => o.StrokeWidth, (o, v) => o.StrokeWidth = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(4000)
            .Minimum(0)
            .SerializeName("stroke-width")
            .Register();

        AffectsRender<Rectangle>(StrokeWidthProperty);
    }

    public float StrokeWidth
    {
        get => _strokeWidth;
        set => SetAndRaise(StrokeWidthProperty, ref _strokeWidth, value);
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
            canvas.DrawRect(new Size(Width, Height));
        }
    }
}
