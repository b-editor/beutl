using Avalonia;
using Beutl.Media;
using Color = Avalonia.Media.Color;

namespace Beutl.Controls.PropertyEditors;

// A grading color as an offset from its neutral value, split into BT.709 luma and Cb/Cr chroma: the axes the
// vectorscope draws, so moving a wheel's puck toward a color moves the scope's trace the same way.
internal static class ColorBalance
{
    // The luma weights of the ColorGrading shader and the vectorscope.
    private const float Kr = 0.2126f;
    private const float Kb = 0.0722f;
    private const float Kg = 1 - Kr - Kb;

    // Cb and Cr span -0.5 to 0.5 over the RGB cube.
    private const float CbScale = 2 * (1 - Kb);
    private const float CrScale = 2 * (1 - Kr);

    public static float Luma(GradingColor color) => (Kr * color.R) + (Kg * color.G) + (Kb * color.B);

    // The Cb (X) and Cr (Y) of the color's offset from neutral.
    public static Vector GetChroma(GradingColor color, GradingColor neutral)
    {
        GradingColor offset = color - neutral;
        float y = Luma(offset);
        return new Vector((offset.B - y) / CbScale, (offset.R - y) / CrScale);
    }

    // The offset of R, G and B that moves the chroma by (Cb, Cr) and keeps the luma.
    public static GradingColor ChromaOffset(Vector chroma)
    {
        float r = (float)(CrScale * chroma.Y);
        float b = (float)(CbScale * chroma.X);
        float g = ((-Kr * r) - (Kb * b)) / Kg;
        return new GradingColor(r, g, b);
    }

    // The color with the chroma's luma kept and its chroma removed.
    public static GradingColor WithoutChroma(GradingColor color, GradingColor neutral)
        => color - ChromaOffset(GetChroma(color, neutral));

    // The most saturated color in the direction of the chroma, for drawing the wheel; gray for no direction.
    public static Color HueColor(Vector chroma)
    {
        GradingColor offset = ChromaOffset(chroma);
        float min = MathF.Min(offset.R, MathF.Min(offset.G, offset.B));
        float max = MathF.Max(offset.R, MathF.Max(offset.G, offset.B));
        float range = max - min;
        if (range < 1e-6f)
            return Color.FromRgb(128, 128, 128);

        return Color.FromRgb(ToByte((offset.R - min) / range), ToByte((offset.G - min) / range), ToByte((offset.B - min) / range));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
