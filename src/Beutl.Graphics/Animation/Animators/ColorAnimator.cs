using Beutl.Media;

namespace Beutl.Animation.Animators;

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

    public override Color Interpolate(float progress, Color oldValue, Color newValue)
    {
        return InterpolateCore(progress, oldValue, newValue);
    }

    internal static Color InterpolateCore(float progress, Color oldValue, Color newValue)
    {
        // normalize sRGB values.
        var oldA = oldValue.A / 255f;
        var oldR = oldValue.R / 255f;
        var oldG = oldValue.G / 255f;
        var oldB = oldValue.B / 255f;

        var newA = newValue.A / 255f;
        var newR = newValue.R / 255f;
        var newG = newValue.G / 255f;
        var newB = newValue.B / 255f;

        // convert from sRGB to linear
        oldR = EOCF_sRGB(oldR);
        oldG = EOCF_sRGB(oldG);
        oldB = EOCF_sRGB(oldB);

        newR = EOCF_sRGB(newR);
        newG = EOCF_sRGB(newG);
        newB = EOCF_sRGB(newB);

        // compute the interpolated color in linear space
        var a = oldA + progress * (newA - oldA);
        var r = oldR + progress * (newR - oldR);
        var g = oldG + progress * (newG - oldG);
        var b = oldB + progress * (newB - oldB);

        // convert back to sRGB in the [0..255] range
        a *= 255f;
        r = OECF_sRGB(r) * 255f;
        g = OECF_sRGB(g) * 255f;
        b = OECF_sRGB(b) * 255f;

        return Color.FromArgb((byte)MathF.Round(a), (byte)MathF.Round(r), (byte)MathF.Round(g), (byte)MathF.Round(b));
    }
}