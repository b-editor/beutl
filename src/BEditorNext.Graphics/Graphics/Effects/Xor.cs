using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Xor : PixelEffect
{
    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        pixel.B = (byte)(pixel.B ^ 128);
        pixel.G = (byte)(pixel.G ^ 128);
        pixel.R = (byte)(pixel.R ^ 128);
    }
}
