using Beutl.Utilities;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Provides CurrentPixel equivalents of Skia built-in color filters whose behavior is not expressible as a
/// color matrix.
/// </summary>
internal static class BuiltInColorFilterShader
{
    private const string LumaColorSource =
        """
        half4 apply(half4 color) {
            // CurrentPixel input is premultiplied, so this dot product includes the source alpha. That is the
            // defining difference between Skia's LumaColor and a luminance-to-alpha color matrix.
            half luma = saturate(dot(half3(0.2126, 0.7152, 0.0722), color.rgb));
            return half4(0.0, 0.0, 0.0, luma);
        }
        """;

    private const string HighContrastSource =
        """
        uniform half grayscale;
        uniform half invertStyle;
        uniform half contrast;

        half3 hslToRgb(half3 hsl) {
            half chroma = (1.0 - abs(2.0 * hsl.z - 1.0)) * hsl.y;
            half3 p = hsl.xxx + half3(0.0, 2.0 / 3.0, 1.0 / 3.0);
            half3 q = saturate(abs(fract(p) * 6.0 - 3.0) - 1.0);
            return (q - 0.5) * chroma + hsl.z;
        }

        half3 rgbToHsl(half3 color) {
            half maximum = max(max(color.r, color.g), color.b);
            half minimum = min(min(color.r, color.g), color.b);
            half delta = maximum - minimum;
            half inverseDelta = 1.0 / delta;
            half greenLessThanBlue = color.g < color.b ? 6.0 : 0.0;
            half hue = (1.0 / 6.0) * (maximum == minimum
                ? 0.0
                : color.r >= color.g && color.r >= color.b
                    ? inverseDelta * (color.g - color.b) + greenLessThanBlue
                    : color.g >= color.b
                        ? inverseDelta * (color.b - color.r) + 2.0
                        : inverseDelta * (color.r - color.g) + 4.0);
            half sum = maximum + minimum;
            half lightness = sum * 0.5;
            half saturation = maximum == minimum
                ? 0.0
                : delta / (lightness > 0.5 ? 2.0 - sum : sum);
            return half3(hue, saturation, lightness);
        }

        half4 apply(half4 color) {
            // Skia evaluates HighContrast in a linear, unpremultiplied working format. CurrentPixel receives
            // linear premultiplied pixels, so make that conversion explicit and restore premultiplication below.
            half4 straight = unpremul(color);
            half3 transformed = straight.rgb;
            if (grayscale == 1.0) {
                transformed = dot(half3(0.2126, 0.7152, 0.0722), transformed).rrr;
            }
            if (invertStyle == 1.0) {
                transformed = 1.0 - transformed;
            } else if (invertStyle == 2.0) {
                transformed = rgbToHsl(transformed);
                transformed.b = 1.0 - transformed.b;
                transformed = hslToRgb(transformed);
            }
            transformed = mix(half3(0.5), transformed, contrast);
            return half4(saturate(transformed) * color.a, color.a);
        }
        """;

    private static readonly SkslSource s_lumaColorSource =
        new(LumaColorSource, ShaderDescriptionKind.CurrentPixel);

    private static readonly SkslSource s_highContrastSource =
        new(HighContrastSource, ShaderDescriptionKind.CurrentPixel);

    internal static ShaderDescription LumaColor()
        => ShaderDescription.CurrentPixel(s_lumaColorSource, bindings: null);

    internal static ShaderDescription HighContrast(
        bool grayscale,
        HighContrastInvertStyle invertStyle,
        float contrast)
    {
        float pinned = Math.Clamp(
            contrast,
            -1f + MathUtilities.FloatEpsilon,
            1f - MathUtilities.FloatEpsilon);
        float contrastScale = (1f + pinned) / (1f - pinned);
        return ShaderDescription.CurrentPixel(
            s_highContrastSource,
            bindings =>
            {
                bindings.Uniform("grayscale", grayscale ? 1f : 0f);
                bindings.Uniform("invertStyle", (float)invertStyle);
                bindings.Uniform("contrast", contrastScale);
            });
    }
}
