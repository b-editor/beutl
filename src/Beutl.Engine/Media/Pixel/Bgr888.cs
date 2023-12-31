using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgr888(byte r, byte g, byte b) : IPixel<Bgr888>
{
    public byte B = b;

    public byte G = g;

    public byte R = r;

    public Bgr888 FromColor(Color color)
    {
        return new Bgr888(color.R, color.G, color.B);
    }

    public Color ToColor()
    {
        return Color.FromArgb(255, R, G, B);
    }
}
