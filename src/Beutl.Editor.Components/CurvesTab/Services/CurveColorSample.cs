using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Editor.Components.CurvesTab.Services;

// A color read from a frame in the terms the Curves effect reads it: the linear working color encoded as sRGB, and the
// hue, saturation and value its shader computes from that. Red, Green and Blue are the encoded values.
internal readonly record struct CurveColorSample(float Red, float Green, float Blue, float Hue, float Saturation, float Value)
{
    // Less coverage than this is no color to pick: the effect leaves such pixels as they are.
    private const float MinAlpha = 1f / 255;

    // The average of the pixels within radius of (x, y), or null where they hold almost nothing. The average is taken
    // as the renderer blends, premultiplied in linear light, so a pixel counts by its coverage.
    public static CurveColorSample? Read(Bitmap frame, int x, int y, int radius = 1)
    {
        if (frame.ColorType != BitmapColorType.RgbaF16
            || frame.AlphaType != BitmapAlphaType.Premul
            || frame.ColorSpace != BitmapColorSpace.LinearSrgb)
        {
            using Bitmap converted = frame.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
            return Read(converted, x, y, radius);
        }

        float red = 0, green = 0, blue = 0, alpha = 0;
        int count = 0;
        for (int row = Math.Max(0, y - radius); row <= Math.Min(frame.Height - 1, y + radius); row++)
        {
            ReadOnlySpan<RgbaF16> pixels = frame.GetRow<RgbaF16>(row);
            for (int column = Math.Max(0, x - radius); column <= Math.Min(frame.Width - 1, x + radius); column++)
            {
                RgbaF16 pixel = pixels[column];
                red += (float)pixel.R;
                green += (float)pixel.G;
                blue += (float)pixel.B;
                alpha += (float)pixel.A;
                count++;
            }
        }

        if (count == 0 || !(alpha / count >= MinAlpha))
            return null;

        return FromLinear(red / alpha, green / alpha, blue / alpha);
    }

    // As the effect's shader does: linearToSrgb, then rgb_to_hsv.
    public static CurveColorSample FromLinear(float red, float green, float blue)
    {
        float r = Encode(red);
        float g = Encode(green);
        float b = Encode(blue);

        (float p0, float p1, float p2, float p3) = g >= b ? (g, b, 0f, -1f / 3) : (b, g, -1f, 2f / 3);
        (float q0, float q1, float q2, float q3) = r >= p0 ? (r, p1, p2, p0) : (p0, p1, p3, r);
        float d = q0 - Math.Min(q3, q1);
        const float e = 1e-10f;
        return new CurveColorSample(r, g, b, MathF.Abs(q2 + (q3 - q1) / (6 * d + e)), d / (q0 + e), q0);
    }

    // Where this color stands on a hue curve, from 0 to 1. Hue vs hue reads the hue itself; hue vs saturation and hue vs
    // luminance read it after hue vs hue has turned it.
    public float HueOn(CurveMap? hueVsHue)
    {
        float hue = Hue - MathF.Floor(Hue);
        if (hueVsHue is null)
            return hue;

        hue += hueVsHue.Evaluate(hue) - 0.5f + 1;
        return hue - MathF.Floor(hue);
    }

    public Color ToColor()
        => Color.FromArgb(255, ToByte(Red), ToByte(Green), ToByte(Blue));

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);

    private static float Encode(float c)
        => c >= 0.0031308f ? 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f : c * 12.92f;
}
