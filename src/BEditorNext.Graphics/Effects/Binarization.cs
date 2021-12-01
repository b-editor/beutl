using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Binarization : IPixelEffect
{
    public byte Value { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        var value = Value;
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
