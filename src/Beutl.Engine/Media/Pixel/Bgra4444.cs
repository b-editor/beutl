using System.Runtime.InteropServices;

using Beutl.Media;

namespace Beutl.Media.Pixel;

[StructLayout(LayoutKind.Sequential)]
public struct Bgra4444(ushort value) : IPixel<Bgra4444>
{
    public ushort Value = value;

    public Bgra4444 FromColor(Color color)
    {
        return new Bgra4444(
            (ushort)(((int)Math.Round(color.A / 255F * 15F) & 0x0F) << 12
            | ((int)Math.Round(color.R / 255F * 15F) & 0x0F) << 8
            | ((int)Math.Round(color.G / 255F * 15F) & 0x0F) << 4
            | (int)Math.Round(color.B / 255F * 15F) & 0x0F));
    }

    public Color ToColor()
    {
        const float Max = 15F;

        return Color.FromArgb(
            (byte)((Value >> 12 & 0x0F) * Max),
            (byte)((Value >> 8 & 0x0F) * Max),
            (byte)((Value >> 4 & 0x0F) * Max),
            (byte)((Value & 0x0F) * Max));
    }
}
