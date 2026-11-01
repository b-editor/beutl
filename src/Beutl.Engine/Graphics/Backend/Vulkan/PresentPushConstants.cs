using System.Runtime.InteropServices;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Push constants for the present pipeline.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PresentPushConstants
{
    public float DstX;
    public float DstY;
    public float DstW;
    public float DstH;
    public float Exposure;
    public int TmOperator; // 0=None, 1=Reinhard, 2=ACES, 3=Hable, 4=FitToDisplay
    public int LinearToSrgb;      // 1 = LinearからGammaへの変換が必要
    public int IsSourceLinear; // 1 = Linear, 0 = Gamma

    // The PreviewDisplayMapping FitToDisplay applies, field by field; a vec4 in the shader.
    public float MapKnee;
    public float MapKs;
    public float MapMaxLum;
    public float MapSourcePq;

    // The PreviewGamutMapping FitToDisplay applies: a vec4 per row of the sRGB-to-display matrix, whose w carries
    // 1 to map colors into the display's gamut (row 0) and 1 to keep them within sRGB as well (row 1).
    public float Gamut0X;
    public float Gamut0Y;
    public float Gamut0Z;
    public float GamutEnabled;
    public float Gamut1X;
    public float Gamut1Y;
    public float Gamut1Z;
    public float GamutWithinSrgb;
    public float Gamut2X;
    public float Gamut2Y;
    public float Gamut2Z;
    public float Gamut2W; // Unused; fills the vec4.

    // PresentLuminance.ReferenceWhite: what reference white (1.0) is written as.
    public float ReferenceWhite;

    public int OutputBt2020; // 1 = the swapchain takes linear BT.2020 rather than linear sRGB
}
