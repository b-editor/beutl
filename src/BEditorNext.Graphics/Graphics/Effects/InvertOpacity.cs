using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class InvertOpacity : PixelEffect
{
    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        pixel.A = (byte)(255 - pixel.A);
    }
}
