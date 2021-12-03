using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class SetOpacity : IPixelEffect
{
    public float Opacity { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        pixel.A = (byte)(pixel.A * Opacity);
    }
}
