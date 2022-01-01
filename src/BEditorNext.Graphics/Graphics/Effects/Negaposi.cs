using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Negaposi : PixelEffect
{
    public byte Red { get; set; }

    public byte Green { get; set; }

    public byte Blue { get; set; }

    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        pixel.B = (byte)(Blue - pixel.B);
        pixel.G = (byte)(Green - pixel.G);
        pixel.R = (byte)(Red - pixel.R);
    }
}
