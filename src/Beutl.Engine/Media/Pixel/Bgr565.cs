using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgr565(ushort value) : IPixel<Bgr565>
{
    public ushort Value = value;

    public readonly Bgr565 FromColor(Color color)
    {
        int b = color.B >> 3 & 0x1f;
        int g = (color.G >> 2 & 0x3f) << 5;
        int r = (color.R >> 3 & 0x1f) << 11;

        return new Bgr565((ushort)(r | g | b));
    }

    public readonly Color ToColor()
    {
        int b = (Value & 0x1f) << 3;
        int g = (Value >> 5 & 0x3f) << 2;
        int r = (Value >> 11 & 0x1f) << 3;

        return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
    }
}
