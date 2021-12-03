using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ColorAdjust : IPixelEffect
{
    public short Red { get; set; }

    public short Green { get; set; }

    public short Blue { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        pixel.B = (byte)Helper.Set255(pixel.B + Blue);
        pixel.G = (byte)Helper.Set255(pixel.G + Green);
        pixel.R = (byte)Helper.Set255(pixel.R + Red);
    }
}
