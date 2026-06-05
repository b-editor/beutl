using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Image-quality metrics over RgbaF16 (linear) bitmaps for the feature-003 golden suite.
// Pure CPU math — no GPU required, so these are unit-testable on their own.
internal static class ImageMetrics
{
    // ITU-R BT.709 luma weights, applied in linear light.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>Mean absolute error over the RGB channels of two same-size RgbaF16 bitmaps (linear).</summary>
    public static double MeanAbsoluteError(Bitmap a, Bitmap b)
    {
        EnsureComparable(a, b);
        ReadOnlySpan<ushort> pa = a.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> pb = b.GetPixelSpan<ushort>();
        int pixels = a.Width * a.Height;

        double sum = 0;
        for (int i = 0; i < pixels; i++)
        {
            int o = i * 4;
            for (int c = 0; c < 3; c++)
            {
                float va = HalfBitsToFloat(pa[o + c]);
                float vb = HalfBitsToFloat(pb[o + c]);
                sum += Math.Abs(va - vb);
            }
        }

        return sum / (pixels * 3);
    }

    /// <summary>
    /// Global (single-window) SSIM over linear luminance of two same-size bitmaps. Returns 1.0 for
    /// identical inputs. Global SSIM is a conservative proxy sufficient for golden gating.
    /// </summary>
    public static double Ssim(Bitmap a, Bitmap b)
    {
        EnsureComparable(a, b);
        ReadOnlySpan<ushort> pa = a.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> pb = b.GetPixelSpan<ushort>();
        int pixels = a.Width * a.Height;

        double meanA = 0, meanB = 0;
        for (int i = 0; i < pixels; i++)
        {
            meanA += Luma(pa, i);
            meanB += Luma(pb, i);
        }

        meanA /= pixels;
        meanB /= pixels;

        double varA = 0, varB = 0, cov = 0;
        for (int i = 0; i < pixels; i++)
        {
            double da = Luma(pa, i) - meanA;
            double db = Luma(pb, i) - meanB;
            varA += da * da;
            varB += db * db;
            cov += da * db;
        }

        varA /= pixels;
        varB /= pixels;
        cov /= pixels;

        const double c1 = 0.01 * 0.01;
        const double c2 = 0.03 * 0.03;
        double num = (2 * meanA * meanB + c1) * (2 * cov + c2);
        double den = (meanA * meanA + meanB * meanB + c1) * (varA + varB + c2);
        return num / den;
    }

    /// <summary>
    /// Mean squared gradient (horizontal + vertical adjacent luminance differences) — a proxy for
    /// high-frequency / aliasing energy. A supersampled render should report less than a 1:1 render.
    /// </summary>
    public static double AliasingEnergy(Bitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        ReadOnlySpan<ushort> p = bitmap.GetPixelSpan<ushort>();

        double sum = 0;
        long count = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double l = Luma(p, i);
                if (x + 1 < w)
                {
                    double d = l - Luma(p, i + 1);
                    sum += d * d;
                    count++;
                }

                if (y + 1 < h)
                {
                    double d = l - Luma(p, i + w);
                    sum += d * d;
                    count++;
                }
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    private static double Luma(ReadOnlySpan<ushort> px, int pixelIndex)
    {
        int o = pixelIndex * 4;
        return LumaR * HalfBitsToFloat(px[o]) + LumaG * HalfBitsToFloat(px[o + 1]) + LumaB * HalfBitsToFloat(px[o + 2]);
    }

    private static float HalfBitsToFloat(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);

    private static void EnsureComparable(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException($"Bitmap sizes differ: {a.Width}x{a.Height} vs {b.Width}x{b.Height}.");
        if (a.ColorType != BitmapColorType.RgbaF16 || b.ColorType != BitmapColorType.RgbaF16)
            throw new ArgumentException("ImageMetrics expects RgbaF16 bitmaps (linear).");
    }
}
