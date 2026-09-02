using System.Runtime.InteropServices;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Push constants for the present pipeline.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PresentPushConstants
{
    public float SrcX;
    public float SrcY;
    public float SrcW;
    public float SrcH;
    public float DstX;
    public float DstY;
    public float DstW;
    public float DstH;
    public float Exposure;
    public int TmOperator; // 0=None, 1=Reinhard, 2=ACES, 3=Hable
    public int LinearToSrgb;      // 1 = LinearからGammaへの変換が必要
    public int IsSourceLinear; // 1 = Linear, 0 = Gamma
}
