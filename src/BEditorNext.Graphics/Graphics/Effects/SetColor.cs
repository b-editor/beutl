using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class SetColor : IPixelEffect
{
    public Bgra8888 Color { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        pixel.R = Color.R;
        pixel.G = Color.G;
        pixel.B = Color.B;
    }
}
