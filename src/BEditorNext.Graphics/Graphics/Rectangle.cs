using BEditorNext.Media;

namespace BEditorNext.Graphics;

public sealed class Rectangle : Drawable
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
        canvas.DrawRect(new Size(Width, Height));
    }
}
