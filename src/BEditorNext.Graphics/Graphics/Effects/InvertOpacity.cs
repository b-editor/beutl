using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class InvertOpacity : IPixelEffect
{
    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        pixel.A = (byte)(255 - pixel.A);
    }
}
