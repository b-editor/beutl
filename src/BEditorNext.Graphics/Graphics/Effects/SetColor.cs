using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class SetColor : PixelEffect
{
    public Bgra8888 Color { get; set; }

    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        pixel.R = Color.R;
        pixel.G = Color.G;
        pixel.B = Color.B;
    }
}
