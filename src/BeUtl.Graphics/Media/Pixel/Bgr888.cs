using System.Runtime.InteropServices;

namespace BeUtl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgr888 : IPixel<Bgr888>
{
    public byte B;

    public byte G;

    public byte R;

    public Bgr888(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public Bgr888 FromColor(Color color)
    {
        return new Bgr888(color.R, color.G, color.B);
    }

    public Color ToColor()
    {
        return Color.FromArgb(255, R, G, B);
    }
}
