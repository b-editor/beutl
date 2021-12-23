using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Sepia : PixelEffect
{
    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        double ntsc = Helper.Set255Round(
            (pixel.R * 0.11448) +
            (pixel.G * 0.58661) +
            (pixel.B * 0.29891));

        pixel.B = (byte)Helper.Set255(ntsc - 20);
        pixel.G = (byte)ntsc;
        pixel.R = (byte)Helper.Set255(ntsc + 30);
    }
}
