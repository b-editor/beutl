namespace Beutl.Graphics3D;

[Flags]
public enum ColorComponentFlags : byte
{
    None = 0x0,
    R = 0x1,
    G = 0x2,
    B = 0x4,
    A = 0x08,
    RGB = R | G | B,
    RGBA = R | G | B | A
}
