namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// How the present shader fits Beutl's linear values to the display: the swapchain value it writes for reference
/// white (1.0), and the display's peak, which <see cref="Beutl.Configuration.UIToneMappingOperator.FitToDisplay"/>
/// rolls highlights off into.
/// </summary>
/// <param name="ReferenceWhite">The swapchain value that shows as bright as the UI's white.</param>
/// <param name="Headroom">The display's peak in units of reference white, or 0 when it is not known.</param>
internal readonly record struct PresentLuminance(float ReferenceWhite, float Headroom)
{
    /// <summary>The luminance of scRGB 1.0 on a Windows HDR display.</summary>
    public const float ScRgbWhiteNits = 80f;

    /// <summary>Values pass through, for a display whose luminance is not known.</summary>
    public static PresentLuminance Unknown { get; } = new(1f, 0f);

    /// <summary>A display that shows nothing above 1.0.</summary>
    public static PresentLuminance Sdr { get; } = new(1f, 1f);

    /// <summary>
    /// An scRGB swapchain on a Windows HDR display, which shows 1.0 at 80 nit and the UI's white at
    /// <paramref name="sdrWhiteNits"/>. An unknown white level (0 or less) counts as 80 nit, and an unknown peak leaves
    /// the headroom unknown.
    /// </summary>
    public static PresentLuminance ForScRgbHdr(float sdrWhiteNits, float maxLuminanceNits)
    {
        float white = sdrWhiteNits > 0f ? sdrWhiteNits : ScRgbWhiteNits;
        return new PresentLuminance(white / ScRgbWhiteNits, maxLuminanceNits > 0f ? maxLuminanceNits / white : 0f);
    }
}
