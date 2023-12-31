using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Grayscale8(byte value) : IPixel<Grayscale8>
{
    public byte Value = value;

    public readonly Grayscale8 FromColor(Color color)
    {
        double value = color.R * 0.11448 +
            color.G * 0.58661 +
            color.B * 0.29891;
        double ntsc = value switch
        {
            > 255 => 255,
            < 0 => 0,
            _ => Math.Round(value),
        };

        return new Grayscale8((byte)ntsc);
    }

    public readonly Color ToColor()
    {
        return Color.FromArgb(Value, Value, Value, Value);
    }
}
