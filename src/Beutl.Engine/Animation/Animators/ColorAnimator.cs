using Beutl.Media;

namespace Beutl.Animation.Animators;

public sealed class ColorAnimator : Animator<Color>
{
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
        oldR = Color.SrgbToLinear(oldR);
        oldG = Color.SrgbToLinear(oldG);
        oldB = Color.SrgbToLinear(oldB);

        newR = Color.SrgbToLinear(newR);
        newG = Color.SrgbToLinear(newG);
        newB = Color.SrgbToLinear(newB);

        // compute the interpolated color in linear space
        var a = oldA + progress * (newA - oldA);
        var r = oldR + progress * (newR - oldR);
        var g = oldG + progress * (newG - oldG);
        var b = oldB + progress * (newB - oldB);

        // convert back to sRGB in the [0..255] range
        a *= 255f;
        r = Color.LinearToSrgb(r) * 255f;
        g = Color.LinearToSrgb(g) * 255f;
        b = Color.LinearToSrgb(b) * 255f;

        return Color.FromArgb((byte)MathF.Round(a), (byte)MathF.Round(r), (byte)MathF.Round(g), (byte)MathF.Round(b));
    }
}
