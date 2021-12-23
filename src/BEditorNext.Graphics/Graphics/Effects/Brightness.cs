using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Brightness : PixelEffect
{
    public short Value { get; set; }

    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        short value = Value;
        pixel.B = (byte)Helper.Set255(pixel.B + value);
        pixel.G = (byte)Helper.Set255(pixel.G + value);
        pixel.R = (byte)Helper.Set255(pixel.R + value);
    }
}
