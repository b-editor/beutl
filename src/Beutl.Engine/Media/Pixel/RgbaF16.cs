using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct RgbaF16(Half r, Half g, Half b, Half a)
{
    public Half R = r;

    public Half G = g;

    public Half B = b;

    public Half A = a;

    public readonly RgbaF16 SrgbToLinear()
    {
        return new RgbaF16
        {
            R = (Half)Color.SrgbToLinear((float)R),
            G = (Half)Color.SrgbToLinear((float)G),
            B = (Half)Color.SrgbToLinear((float)B),
            A = A
        };
    }

    public readonly RgbaF16 LinearToSrgb()
    {
        return new RgbaF16
        {
            R = (Half)Color.LinearToSrgb((float)R),
            G = (Half)Color.LinearToSrgb((float)G),
            B = (Half)Color.LinearToSrgb((float)B),
            A = A
        };
    }
}
