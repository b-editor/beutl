using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct RgbaF16(Half r, Half g, Half b, Half a)
{
    public Half R = r;

    public Half G = g;

    public Half B = b;

    public Half A = a;
}
