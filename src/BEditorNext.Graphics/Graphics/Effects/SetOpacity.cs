using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class SetOpacity : PixelEffect
{
    public float Opacity { get; set; }

    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        pixel.A = (byte)(pixel.A * Opacity);
    }
}
