namespace Beutl.Media;

/// <summary>How to read the color code values of an SDR video element.</summary>
public enum VideoTransferInterpretation
{
    /// <summary>Follow the source's color metadata.</summary>
    Auto = 0,
    /// <summary>Use the BT.709 display curve used by QuickTime.</summary>
    QuickTime = 1,
    /// <summary>Use a BT.1886 reference display with gamma 2.4.</summary>
    Bt1886 = 2,
    /// <summary>Use the sRGB display curve.</summary>
    Srgb = 3,
    /// <summary>Use the inverse BT.709 camera curve to recover scene light.</summary>
    Bt709Scene = 4,
}
