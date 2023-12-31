using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgra8888(byte r, byte g, byte b, byte a) : IPixel<Bgra8888>
{
    public byte B = b;

    public byte G = g;

    public byte R = r;

    public byte A = a;

    public Bgra8888 FromColor(Color color)
    {
        return new Bgra8888(color.R, color.G, color.B, color.A);
    }

    public Color ToColor()
    {
        return Color.FromArgb(A, R, G, B);
    }
}
