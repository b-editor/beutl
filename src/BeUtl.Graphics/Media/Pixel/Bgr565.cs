using System.Runtime.InteropServices;

namespace BeUtl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgr565 : IPixel<Bgr565>
{
    public ushort Value;

    public Bgr565(ushort value)
    {
        Value = value;
    }

    public Bgr565 FromColor(Color color)
    {
        var b = color.B >> 3 & 0x1f;
        var g = (color.G >> 2 & 0x3f) << 5;
        var r = (color.R >> 3 & 0x1f) << 11;

        return new Bgr565((ushort)(r | g | b));
    }

    public Color ToColor()
    {
        var b = (Value & 0x1f) << 3;
        var g = (Value >> 5 & 0x3f) << 2;
        var r = (Value >> 11 & 0x1f) << 3;

        return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
    }
}
