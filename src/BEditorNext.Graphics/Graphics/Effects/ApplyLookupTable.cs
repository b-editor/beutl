using System.Numerics;

using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ApplyLookupTable : PixelEffect
{
    public ApplyLookupTable(LookupTable lut)
    {
        LookupTable = lut;
    }

    public LookupTable LookupTable { get; set; }

    public float Strength { get; set; }

    public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
    {
        Span<float> rData, gData, bData;
        if (LookupTable.Dimension == LookupTableDimension.OneDimension)
        {
            rData = gData = bData = LookupTable.AsSpan();
        }
        else
        {
            rData = LookupTable.AsSpan(0);
            gData = LookupTable.AsSpan(1);
            bData = LookupTable.AsSpan(2);
        }
        float scale = LookupTable.Size / 256f;

        float r = pixel.R * scale;
        float g = pixel.G * scale;
        float b = pixel.B * scale;
        var vec = new Vector3(rData[Helper.Near(LookupTable.Size, r)], gData[Helper.Near(LookupTable.Size, g)], bData[Helper.Near(LookupTable.Size, b)]);

        pixel.R = (byte)((((vec.X * 255) + 0.5) * Strength) + (pixel.R * (1 - Strength)));
        pixel.G = (byte)((((vec.Y * 255) + 0.5) * Strength) + (pixel.G * (1 - Strength)));
        pixel.B = (byte)((((vec.Z * 255) + 0.5) * Strength) + (pixel.B * (1 - Strength)));

    }
}
