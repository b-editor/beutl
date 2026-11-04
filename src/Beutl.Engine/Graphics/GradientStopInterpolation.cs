using System.Numerics;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

// Skia mixes a gradient's colors in the color space of the surface, which is linear sRGB here. Its C API takes no other
// space, so a gradient in another space gets stops close enough together that what Skia mixes between them is not seen.
internal static class GradientStopInterpolation
{
    private const int StepsPerSegment = 32;

    public static (SKColor[] Colors, float[] Offsets) Expand(
        SKColor[] colors, float[] offsets, GradientInterpolationSpace space, bool premultiplied)
    {
        return space == GradientInterpolationSpace.LinearSrgb && !premultiplied
            ? (colors, offsets)
            : ExpandForSrgbSurface(colors, offsets, space, premultiplied);
    }

    // For a surface that mixes in sRGB, such as the editor's, where linear sRGB needs the stops as well.
    public static (SKColor[] Colors, float[] Offsets) ExpandForSrgbSurface(
        SKColor[] colors, float[] offsets, GradientInterpolationSpace space, bool premultiplied)
    {
        if (colors.Length < 2)
            return (colors, offsets);

        int count = (colors.Length - 1) * StepsPerSegment + 1;
        var expandedColors = new SKColor[count];
        var expandedOffsets = new float[count];
        for (int segment = 0; segment < colors.Length - 1; segment++)
        {
            Vector4 from = ToSpace(colors[segment], space, premultiplied);
            Vector4 to = ToSpace(colors[segment + 1], space, premultiplied);
            for (int step = 0; step < StepsPerSegment; step++)
            {
                float t = step / (float)StepsPerSegment;
                int index = segment * StepsPerSegment + step;
                expandedColors[index] = FromSpace(Vector4.Lerp(from, to, t), space, premultiplied);
                expandedOffsets[index] = float.Lerp(offsets[segment], offsets[segment + 1], t);
            }
        }

        expandedColors[^1] = colors[^1];
        expandedOffsets[^1] = offsets[^1];
        return (expandedColors, expandedOffsets);
    }

    // X, Y and Z in the space, optionally multiplied by the alpha in W.
    private static Vector4 ToSpace(SKColor color, GradientInterpolationSpace space, bool premultiplied)
    {
        var srgb = new Vector3(color.Red, color.Green, color.Blue) / 255f;
        Vector3 value = space switch
        {
            GradientInterpolationSpace.Srgb => srgb,
            GradientInterpolationSpace.Oklab => LinearToOklab(ToLinear(srgb)),
            _ => ToLinear(srgb),
        };
        float alpha = color.Alpha / 255f;
        return new Vector4(premultiplied ? value * alpha : value, alpha);
    }

    private static SKColor FromSpace(Vector4 mixed, GradientInterpolationSpace space, bool premultiplied)
    {
        float alpha = mixed.W;
        var value = new Vector3(mixed.X, mixed.Y, mixed.Z);
        if (premultiplied)
            value = alpha > 0 ? value / alpha : Vector3.Zero;

        Vector3 srgb = space switch
        {
            GradientInterpolationSpace.Srgb => value,
            GradientInterpolationSpace.Oklab => ToSrgb(OklabToLinear(value)),
            _ => ToSrgb(value),
        };
        return new SKColor(ToByte(srgb.X), ToByte(srgb.Y), ToByte(srgb.Z), ToByte(alpha));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    private static Vector3 ToLinear(Vector3 srgb)
        => new(Color.SrgbToLinear(srgb.X), Color.SrgbToLinear(srgb.Y), Color.SrgbToLinear(srgb.Z));

    private static Vector3 ToSrgb(Vector3 linear)
        => new(Color.LinearToSrgb(linear.X), Color.LinearToSrgb(linear.Y), Color.LinearToSrgb(linear.Z));

    // https://bottosson.github.io/posts/oklab/
    private static Vector3 LinearToOklab(Vector3 c)
    {
        float l = MathF.Cbrt(0.4122214708f * c.X + 0.5363325363f * c.Y + 0.0514459929f * c.Z);
        float m = MathF.Cbrt(0.2119034982f * c.X + 0.6806995451f * c.Y + 0.1073969566f * c.Z);
        float s = MathF.Cbrt(0.0883024619f * c.X + 0.2817188376f * c.Y + 0.6299787005f * c.Z);
        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    private static Vector3 OklabToLinear(Vector3 c)
    {
        float l = c.X + 0.3963377774f * c.Y + 0.2158037573f * c.Z;
        float m = c.X - 0.1055613458f * c.Y - 0.0638541728f * c.Z;
        float s = c.X - 0.0894841775f * c.Y - 1.2914855480f * c.Z;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        return new Vector3(
            +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
            -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
    }
}
