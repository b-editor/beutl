using BEditorNext.Media;

namespace BEditorNext.Animation.Animators;

public sealed class ColorAnimator : Animator<Color>
{
    // Opto-electronic conversion function for the sRGB color space
    // Takes a gamma-encoded sRGB value and converts it to a linear sRGB value
    private static float OECF_sRGB(float linear)
    {
        // IEC 61966-2-1:1999
        return linear <= 0.0031308f ? linear * 12.92f : (MathF.Pow(linear, 1.0f / 2.4f) * 1.055f - 0.055f);
    }

    // Electro-optical conversion function for the sRGB color space
    // Takes a linear sRGB value and converts it to a gamma-encoded sRGB value
    private static float EOCF_sRGB(float srgb)
    {
        // IEC 61966-2-1:1999
        return srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }

    public override Color Multiply(Color left, float right)
    {
        float a = left.A / 255f;
        float r = left.R / 255f;
        float g = left.G / 255f;
        float b = left.B / 255f;

        r = EOCF_sRGB(r) * right;
        g = EOCF_sRGB(g) * right;
        b = EOCF_sRGB(b) * right;

        a *= 255f;
        r = OECF_sRGB(r) * 255f;
        g = OECF_sRGB(g) * 255f;
        b = OECF_sRGB(b) * 255f;

        return Color.FromArgb((byte)MathF.Round(a), (byte)MathF.Round(r), (byte)MathF.Round(g), (byte)MathF.Round(b));
    }
}
