using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;

using SkiaSharp;

namespace BEditorNext.Graphics.Effects;

public sealed class Blur : BitmapEffect
{
    public float SigmaX { get; set; }

    public float SigmaY { get; set; }

    public override void Apply(ref Bitmap<Bgra8888> bitmap)
    {
    }
}
