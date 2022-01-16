namespace BeUtl.Graphics;

public sealed class Rectangle : Drawable
{
    private float _strokeWidth;

    public float StrokeWidth
    {
        get => _strokeWidth;
        set => SetProperty(ref _strokeWidth, value);
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
            canvas.DrawRect(new Size(Width, Height));
        }
    }
}
