using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Binarization : PixelEffect
{
    public byte Value { get; set; }

    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        byte value = Value;
        if (pixel.R <= value &&
            pixel.G <= value &&
            pixel.B <= value)
        {
            pixel = default;
        }
        else
        {
            pixel = new Bgra8888(255, 255, 255, 255);
        }
    }
}
