using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

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
        if (Effects.Count == 0)
        {
            float oldStroke = canvas.StrokeWidth;

            canvas.StrokeWidth = StrokeWidth;

            canvas.DrawRect(new Size(Width, Height));
            canvas.StrokeWidth = oldStroke;
        }
        else
        {
            using Bitmap<Bgra8888> bmp = ToBitmapWithoutEffect();
            using Bitmap<Bgra8888> bmp2 = BitmapEffect.ApplyAll(bmp, Effects);

            canvas.DrawBitmap(bmp2);
        }
    }

    public Bitmap<Bgra8888> ToBitmapWithoutEffect()
    {
        using var g = new Canvas((int)Width, (int)Height);

        g.IsAntialias = IsAntialias;
        g.Color = Foreground;
        g.StrokeWidth = StrokeWidth;
        g.DrawRect(new Size(Width, Height));

        return g.GetBitmap();
    }
}
