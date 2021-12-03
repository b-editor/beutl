using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ColorKey : IPixelEffect
{
    private double _colorNtsc;
    private Color _color;

    public int Value { get; set; }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            _colorNtsc = Helper.Set255Round(
                (_color.R * 0.11448) +
                (_color.G * 0.58661) +
                (_color.B * 0.29891));
        }
    }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        double ntsc = Helper.Set255Round(
            (pixel.R * 0.11448) +
            (pixel.G * 0.58661) +
            (pixel.B * 0.29891));

        if (Math.Abs(_colorNtsc - ntsc) < Value)
        {
            pixel = default;
        }
    }
}
