using BeUtl.Media;

namespace BeUtl.Graphics;

public sealed class Ellipse : Drawable
{
    public float Width { get; set; }

    public float Height { get; set; }

    public float StrokeWidth { get; set; }

    public override PixelSize Size => new((int)Width, (int)Height);

    public override void Dispose()
    {
    }

    protected override void OnDraw(ICanvas canvas)
    {
        canvas.StrokeWidth = StrokeWidth;
        canvas.DrawCircle(new Size(Width, Height));
    }
}
