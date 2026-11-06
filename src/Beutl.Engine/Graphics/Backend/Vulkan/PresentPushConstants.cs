using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Media;

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

    // The PreviewDisplayMapping FitToDisplay applies.
    public Vector4 DisplayMapping; // knee, ks, maxLum, sourcePq

    // The PreviewGamutMapping FitToDisplay applies: the rows of the sRGB-to-display matrix.
    public Vector4 Gamut0; // row 0; w is 1 to map colors into the display's gamut
    public Vector4 Gamut1; // row 1; w is 1 to keep them within sRGB as well
    public Vector4 Gamut2; // row 2; w is unused

    // PresentLuminance.ReferenceWhite: what reference white (1.0) is written as.
    public float ReferenceWhite;

    public int OutputBt2020; // 1 = the swapchain takes linear BT.2020 rather than linear sRGB

    public int Overlay; // 1 = draw the overlay texture, what the UI draws over the frame, on top of the frame

    public int ComposeInGamma; // 1 = blend the frame over black and the overlay over it in sRGB-encoded values

    public int SampleNearest; // 1 = take the texel each pixel falls in, so a zoomed-in frame shows its pixels sharp

    public void SetDisplayMapping(PreviewDisplayMapping m) =>
        DisplayMapping = new Vector4(m.Knee, m.Ks, m.MaxLum, m.SourcePq);

    public void SetGamut(PreviewGamutMapping g)
    {
        Gamut0 = new Vector4(g.ToDisplay0, g.IsEnabled ? 1f : 0f);
        Gamut1 = new Vector4(g.ToDisplay1, g.WithinSrgb ? 1f : 0f);
        Gamut2 = new Vector4(g.ToDisplay2, 0f);
    }
}
