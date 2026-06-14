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
    /// Windowed SSIM: the MINIMUM SSIM over non-overlapping <paramref name="windowSize"/>×<paramref name="windowSize"/>
    /// tiles (linear luminance). Unlike the global single-window <see cref="Ssim"/> — whose mean/variance is
    /// dominated by a large matching background, so a small localized defect (a thin mis-scaled edge band) is
    /// diluted and still clears a high threshold — the worst tile dominates here, so a localized defect cannot
    /// hide. Returns 1.0 for identical inputs. Use ALONGSIDE the global SSIM as a localized-defect floor.
    /// </summary>
    public static double WindowedSsim(Bitmap a, Bitmap b, int windowSize = 16)
    {
        EnsureComparable(a, b);
        if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize));
        ReadOnlySpan<ushort> pa = a.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> pb = b.GetPixelSpan<ushort>();
        int w = a.Width, h = a.Height;

        double min = 1.0;
        for (int ty = 0; ty < h; ty += windowSize)
        {
            for (int tx = 0; tx < w; tx += windowSize)
            {
                int x1 = Math.Min(tx + windowSize, w);
                int y1 = Math.Min(ty + windowSize, h);
                double s = WindowSsim(pa, pb, w, tx, ty, x1, y1);
                if (s < min) min = s;
            }
        }

        return min;
    }

    private static double WindowSsim(
        ReadOnlySpan<ushort> pa, ReadOnlySpan<ushort> pb, int stride, int x0, int y0, int x1, int y1)
    {
        int n = (x1 - x0) * (y1 - y0);
        double meanA = 0, meanB = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = y * stride + x;
                meanA += Luma(pa, i);
                meanB += Luma(pb, i);
            }
        }

        meanA /= n;
        meanB /= n;

        double varA = 0, varB = 0, cov = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = y * stride + x;
                double da = Luma(pa, i) - meanA;
                double db = Luma(pb, i) - meanB;
                varA += da * da;
                varB += db * db;
                cov += da * db;
            }
        }

        varA /= n;
        varB /= n;
        cov /= n;

        const double c1 = 0.01 * 0.01;
        const double c2 = 0.03 * 0.03;
        double num = (2 * meanA * meanB + c1) * (2 * cov + c2);
        double den = (meanA * meanA + meanB * meanB + c1) * (varA + varB + c2);
        return num / den;
    }

    /// <summary>
    /// Mean squared gradient (horizontal + vertical adjacent luminance differences) — a proxy for
    /// high-frequency / aliasing energy. A supersampled render should report less than a 1:1 render.
    /// <para>
    /// <b>DIAGNOSTIC-ONLY (I8):</b> this metric is unit-tested in isolation (<c>ImageMetricsTests</c>) but is
    /// NOT a gate in the supersample suite — on the actual test patterns its difference wobbled within noise, so
    /// <c>ExportSupersampleTests</c> gates on MAE-to-ground-truth (a clearer, monotone signal) plus an SSIM
    /// no-degradation tolerance instead. Keep this for ad-hoc analysis; do not assume the supersample gate uses it.
    /// </para>
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

    /// <summary>
    /// Returns a label + pixel coordinate for the first non-finite (NaN / ±Inf) RGBA component found across
    /// the given bitmaps, or <c>null</c> if every component is finite. A non-finite GPU render makes
    /// <see cref="Ssim"/> return NaN (the SSIM denominator is otherwise always positive), so callers gate on
    /// this before trusting the metric — a non-finite render is a driver/blur artifact, not a parity result.
    /// </summary>
    public static string? FirstNonFinite(params (string label, Bitmap bitmap)[] bitmaps)
    {
        foreach ((string label, Bitmap bitmap) in bitmaps)
        {
            ReadOnlySpan<ushort> px = bitmap.GetPixelSpan<ushort>();
            for (int i = 0; i < px.Length; i++)
            {
                float v = HalfBitsToFloat(px[i]);
                if (!float.IsFinite(v))
                {
                    int pixel = i / 4;
                    return $"{label} (x={pixel % bitmap.Width}, y={pixel / bitmap.Width}, c={i % 4}) = {v}";
                }
            }
        }

        return null;
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
