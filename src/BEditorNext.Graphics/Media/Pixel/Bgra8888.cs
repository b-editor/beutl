using System.Runtime.InteropServices;

namespace BEditorNext.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgra8888 : IPixel<Bgra8888>
{
    public byte B;

    public byte G;

    public byte R;

    public byte A;

    public Bgra8888(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public Bgra8888 FromColor(Color color)
    {
        return new Bgra8888(color.R, color.G, color.B, color.A);
    }

    public Color ToColor()
    {
        return Color.FromArgb(A, R, G, B);
    }
}
