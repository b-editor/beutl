using System.Runtime.InteropServices;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Grayscale8(byte value)
{
    public byte Value = value;
}
