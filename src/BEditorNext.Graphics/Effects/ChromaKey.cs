using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ChromaKey : IPixelEffect
{
    private Hsv _hsv;

    public Color Color
    {
        get => _hsv.ToColor();
        set => _hsv = new Hsv(value);
    }

    public int SaturationRange { get; set; }

    public int HueRange { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        var srcHsv = pixel.ToColor().ToHsv();

        if (Math.Abs(_hsv.H - srcHsv.H) < HueRange &&
            Math.Abs(_hsv.S - srcHsv.S) < SaturationRange)
        {
            pixel = default;
        }
    }
}
