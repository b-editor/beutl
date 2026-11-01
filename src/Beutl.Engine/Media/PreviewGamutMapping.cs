using System.Numerics;

namespace Beutl.Media;

/// <summary>
/// Fits colors the display cannot show into its gamut. A color the display's primaries would need a negative amount of
/// moves toward the gray of its own luminance until that component reaches zero, so brightness and hue hold where
/// clipping each channel would shift the hue. Colors the display can show stay as they are.
/// </summary>
/// <remarks>
/// The rows convert linear sRGB, the preview's working space, to the display's linear RGB with white kept at white.
/// Moving toward the gray is the same straight line in both, so a color moves in sRGB by the factor the display's
/// components call for.
/// </remarks>
/// <param name="ToDisplay0">The first row of the sRGB-to-display matrix.</param>
/// <param name="ToDisplay1">Its second row.</param>
/// <param name="ToDisplay2">Its third row.</param>
/// <param name="WithinSrgb">
/// Whether colors have to fit sRGB as well, because something after the preview clips there.
/// </param>
internal readonly record struct PreviewGamutMapping(
    Vector3 ToDisplay0, Vector3 ToDisplay1, Vector3 ToDisplay2, bool WithinSrgb)
{
    /// <summary>
    /// The functions both preview paths compile, the HDR present shader (GLSL) and the SDR preview effect (SkSL), so
    /// they keep to the scalar subset the two languages share. <c>gamutSaturation</c> takes a color's components in
    /// the display's RGB and its luminance, and returns the factor to scale the color's distance from its gray by.
    /// </summary>
    public const string ShaderSource = """
        // The luminance of linear sRGB, which any RGB space sharing its D65 white agrees on.
        float gamutLuminance(float r, float g, float b) {
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        float gamutSaturation(float r, float g, float b, float lum) {
            float lowest = min(r, min(g, b));
            if (lowest >= 0.0) return 1.0;
            if (lum <= 0.0) return 0.0;
            return lum / (lum - lowest);
        }
        """;

    /// <summary>Leaves every color as it is, for a display whose gamut is unknown.</summary>
    public static PreviewGamutMapping None => default;

    /// <summary>Fits colors into sRGB, all an 8-bit sRGB surface holds.</summary>
    public static PreviewGamutMapping Srgb { get; } = new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, false);

    public bool IsEnabled => this != None;

    /// <summary>
    /// Returns the mapping into the gamut of a display with <paramref name="primaries"/>, and into sRGB as well when
    /// <paramref name="withinSrgb"/> is set, or <see cref="None"/> when the primaries do not describe a display.
    /// </summary>
    public static PreviewGamutMapping For(DisplayPrimaries primaries, bool withinSrgb)
    {
        if (!primaries.IsValid)
            return None;

        // The preview and the compositors take D65 as white, so the display's matrix does too, whatever white point
        // the display reports.
        BitmapColorSpaceXyz display = BitmapColorSpaceXyz.FromPrimaries(
            primaries.RedX, primaries.RedY, primaries.GreenX, primaries.GreenY, primaries.BlueX, primaries.BlueY,
            0.3127, 0.3290);
        BitmapColorSpaceXyz toDisplay = BitmapColorSpaceXyz.Concat(display.Invert(), BitmapColorSpaceXyz.Bt709);
        return new PreviewGamutMapping(
            new Vector3(toDisplay[0, 0], toDisplay[0, 1], toDisplay[0, 2]),
            new Vector3(toDisplay[1, 0], toDisplay[1, 1], toDisplay[1, 2]),
            new Vector3(toDisplay[2, 0], toDisplay[2, 1], toDisplay[2, 2]),
            withinSrgb);
    }
}
