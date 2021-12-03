using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class Negaposi : IPixelEffect
{
    public byte Red { get; set; }

    public byte Green { get; set; }

    public byte Blue { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        pixel.B = (byte)(Blue - pixel.B);
        pixel.G = (byte)(Green - pixel.G);
        pixel.R = (byte)(Red - pixel.R);
    }
}
