using System.Numerics;
using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ApplyLookupTable : IPixelEffect
{
    public ApplyLookupTable(LookupTable lut)
    {
        LookupTable = lut;
    }

    public LookupTable LookupTable { get; set; }

    public float Strength { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
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
        var scale = LookupTable.Size / 256f;

        var r = pixel.R * scale;
        var g = pixel.G * scale;
        var b = pixel.B * scale;
        var vec = new Vector3(rData[Helper.Near(LookupTable.Size, r)], gData[Helper.Near(LookupTable.Size, g)], bData[Helper.Near(LookupTable.Size, b)]);

        pixel.R = (byte)((((vec.X * 255) + 0.5) * Strength) + (pixel.R * (1 - Strength)));
        pixel.G = (byte)((((vec.Y * 255) + 0.5) * Strength) + (pixel.G * (1 - Strength)));
        pixel.B = (byte)((((vec.Z * 255) + 0.5) * Strength) + (pixel.B * (1 - Strength)));

    }
}
