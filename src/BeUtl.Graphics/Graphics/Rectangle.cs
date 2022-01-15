using BeUtl.Media;

namespace BeUtl.Graphics;

public sealed class Rectangle : Drawable
{
    private float _strokeWidth;
    private float _height;
    private float _width;

    public float Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public float Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public float StrokeWidth
    {
        get => _strokeWidth;
        set => SetProperty(ref _strokeWidth, value);
    }

    public override PixelSize Size => new((int)Width, (int)Height);

    public override void Dispose()
    {
    }

    protected override void OnDraw(ICanvas canvas)
    {
        canvas.StrokeWidth = StrokeWidth;
        canvas.DrawRect(new Size(Width, Height));
    }
}
