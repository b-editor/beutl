using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Brightness : IPixelEffect
{
    public short Value { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        var value = Value;
        pixel.B = (byte)Helper.Set255(pixel.B + value);
        pixel.G = (byte)Helper.Set255(pixel.G + value);
        pixel.R = (byte)Helper.Set255(pixel.R + value);
    }
}
