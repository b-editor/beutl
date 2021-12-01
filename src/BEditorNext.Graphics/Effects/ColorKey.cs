using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ColorKey : IPixelEffect
{
    private double _colorNtsc;
    private Color color;

    public int Value { get; set; }

    public Color Color
    {
        get => color;
        set
        {
            color = value;
            _colorNtsc = Helper.Set255Round(
                (color.R * 0.11448) +
                (color.G * 0.58661) +
                (color.B * 0.29891));
        }
    }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        var ntsc = Helper.Set255Round(
            (pixel.R * 0.11448) +
            (pixel.G * 0.58661) +
            (pixel.B * 0.29891));

        if (Math.Abs(_colorNtsc - ntsc) < Value)
        {
            pixel = default;
        }
    }
}
