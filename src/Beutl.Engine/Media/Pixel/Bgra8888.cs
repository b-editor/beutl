using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgra8888(byte r, byte g, byte b, byte a)
{
    public byte B = b;

    public byte G = g;

    public byte R = r;

    public byte A = a;
}
