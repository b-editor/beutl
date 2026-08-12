using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

internal readonly record struct RgbaMaximumError(double Red, double Green, double Blue, double Alpha)
{
    public double Maximum => Math.Max(Math.Max(Red, Green), Math.Max(Blue, Alpha));

    public double this[int channel] => channel switch
    {
        0 => Red,
        1 => Green,
        2 => Blue,
        3 => Alpha,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}

// Image-quality metrics over linear-premultiplied RgbaF16 bitmaps. Pure CPU math.
internal static class ImageMetrics
{
    private const int ChannelCount = 4;

    // ITU-R BT.709 luma weights, applied in linear light.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>Mean absolute error over the RGB channels of two same-size RgbaF16 bitmaps (linear).</summary>
    public static double MeanAbsoluteError(Bitmap a, Bitmap b)
    {
        EnsureComparable(a, b);

        double sum = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = 0; x < a.Width; x++)
            {
                int offset = x * ChannelCount;
                for (int channel = 0; channel < 3; channel++)
                {
                    sum += Math.Abs(
                        HalfBitsToFloat(rowA[offset + channel]) - HalfBitsToFloat(rowB[offset + channel]));
                }
            }
        }

        return sum / ((double)a.Width * a.Height * 3);
    }

    /// <summary>
    /// Mean absolute error over alpha. RGB MAE and luminance SSIM intentionally do not include this channel.
    /// </summary>
    public static double AlphaMeanAbsoluteError(Bitmap a, Bitmap b)
    {
        EnsureComparable(a, b);

        double sum = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = 0; x < a.Width; x++)
            {
                int alpha = x * ChannelCount + 3;
                sum += Math.Abs(HalfBitsToFloat(rowA[alpha]) - HalfBitsToFloat(rowB[alpha]));
            }
        }

        return sum / ((double)a.Width * a.Height);
    }

    /// <summary>Returns the largest absolute error independently for each RGBA channel.</summary>
    public static RgbaMaximumError MaximumAbsoluteErrorPerChannel(Bitmap a, Bitmap b)
    {
        EnsureComparable(a, b);

        double red = 0;
        double green = 0;
        double blue = 0;
        double alpha = 0;

        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = 0; x < a.Width; x++)
            {
                int offset = x * ChannelCount;
                red = Math.Max(red, AbsoluteError(rowA[offset], rowB[offset]));
                green = Math.Max(green, AbsoluteError(rowA[offset + 1], rowB[offset + 1]));
                blue = Math.Max(blue, AbsoluteError(rowA[offset + 2], rowB[offset + 2]));
                alpha = Math.Max(alpha, AbsoluteError(rowA[offset + 3], rowB[offset + 3]));
            }
        }

        return new RgbaMaximumError(red, green, blue, alpha);
    }

    /// <summary>
    /// Computes RGBA MAE over the nontrivial coverage band in <paramref name="reference"/>.
    /// </summary>
    /// <remarks>
    /// Coverage bounds are exclusive. Deriving the mask only from the frozen reference keeps the oracle independent
    /// of the implementation under test. A reference with no qualifying pixel is rejected so an edge-specific
    /// assertion cannot pass vacuously.
    /// </remarks>
    public static double EdgeBandMeanAbsoluteError(
        Bitmap reference,
        Bitmap actual,
        float minimumCoverage = 0,
        float maximumCoverage = 1)
    {
        EnsureComparable(reference, actual);
        ValidateCoverageBounds(minimumCoverage, maximumCoverage);

        double sum = 0;
        long pixelCount = 0;
        for (int y = 0; y < reference.Height; y++)
        {
            ReadOnlySpan<ushort> referenceRow = reference.GetRow<ushort>(y);
            ReadOnlySpan<ushort> actualRow = actual.GetRow<ushort>(y);
            for (int x = 0; x < reference.Width; x++)
            {
                int offset = x * ChannelCount;
                if (!IsInCoverageBand(referenceRow, offset, minimumCoverage, maximumCoverage))
                    continue;

                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    sum += AbsoluteError(referenceRow[offset + channel], actualRow[offset + channel]);
                }

                pixelCount++;
            }
        }

        if (pixelCount == 0)
            throw new InvalidOperationException("The reference bitmap contains no pixel in the requested coverage band.");

        return sum / (pixelCount * ChannelCount);
    }

    /// <summary>
    /// Returns the largest absolute error independently for each RGBA channel over the reference coverage band.
    /// </summary>
    public static RgbaMaximumError EdgeBandMaximumAbsoluteErrorPerChannel(
        Bitmap reference,
        Bitmap actual,
        float minimumCoverage = 0,
        float maximumCoverage = 1)
    {
        EnsureComparable(reference, actual);
        ValidateCoverageBounds(minimumCoverage, maximumCoverage);

        double red = 0;
        double green = 0;
        double blue = 0;
        double alpha = 0;
        long pixelCount = 0;

        for (int y = 0; y < reference.Height; y++)
        {
            ReadOnlySpan<ushort> referenceRow = reference.GetRow<ushort>(y);
            ReadOnlySpan<ushort> actualRow = actual.GetRow<ushort>(y);
            for (int x = 0; x < reference.Width; x++)
            {
                int offset = x * ChannelCount;
                if (!IsInCoverageBand(referenceRow, offset, minimumCoverage, maximumCoverage))
                    continue;

                red = Math.Max(red, AbsoluteError(referenceRow[offset], actualRow[offset]));
                green = Math.Max(green, AbsoluteError(referenceRow[offset + 1], actualRow[offset + 1]));
                blue = Math.Max(blue, AbsoluteError(referenceRow[offset + 2], actualRow[offset + 2]));
                alpha = Math.Max(alpha, AbsoluteError(referenceRow[offset + 3], actualRow[offset + 3]));
                pixelCount++;
            }
        }

        if (pixelCount == 0)
            throw new InvalidOperationException("The reference bitmap contains no pixel in the requested coverage band.");

        return new RgbaMaximumError(red, green, blue, alpha);
    }

    /// <summary>Global SSIM over linear luminance. Returns 1.0 for identical inputs.</summary>
    public static double Ssim(Bitmap a, Bitmap b)
    {
        EnsureComparable(a, b);

        double meanA = 0;
        double meanB = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = 0; x < a.Width; x++)
            {
                meanA += Luma(rowA, x);
                meanB += Luma(rowB, x);
            }
        }

        double pixels = (double)a.Width * a.Height;
        meanA /= pixels;
        meanB /= pixels;

        double varianceA = 0;
        double varianceB = 0;
        double covariance = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = 0; x < a.Width; x++)
            {
                double deltaA = Luma(rowA, x) - meanA;
                double deltaB = Luma(rowB, x) - meanB;
                varianceA += deltaA * deltaA;
                varianceB += deltaB * deltaB;
                covariance += deltaA * deltaB;
            }
        }

        varianceA /= pixels;
        varianceB /= pixels;
        covariance /= pixels;

        return Ssim(meanA, meanB, varianceA, varianceB, covariance);
    }

    /// <summary>
    /// Minimum SSIM over non-overlapping tiles. A localized defect cannot hide in the global average.
    /// </summary>
    public static double WindowedSsim(Bitmap a, Bitmap b, int windowSize = 16)
    {
        EnsureComparable(a, b);
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize));

        double minimum = 1;
        for (int top = 0; top < a.Height; top += windowSize)
        {
            for (int left = 0; left < a.Width; left += windowSize)
            {
                int right = Math.Min(left + windowSize, a.Width);
                int bottom = Math.Min(top + windowSize, a.Height);
                minimum = Math.Min(minimum, WindowSsim(a, b, left, top, right, bottom));
            }
        }

        return minimum;
    }

    /// <summary>
    /// Mean squared gradient (adjacent luminance differences) -- a proxy for aliasing energy. Diagnostic only.
    /// </summary>
    public static double AliasingEnergy(Bitmap bitmap)
    {
        EnsureSupported(bitmap, nameof(bitmap));

        double sum = 0;
        long count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            ReadOnlySpan<ushort> nextRow = y + 1 < bitmap.Height
                ? bitmap.GetRow<ushort>(y + 1)
                : default;

            for (int x = 0; x < bitmap.Width; x++)
            {
                double luminance = Luma(row, x);
                if (x + 1 < bitmap.Width)
                {
                    double difference = luminance - Luma(row, x + 1);
                    sum += difference * difference;
                    count++;
                }

                if (y + 1 < bitmap.Height)
                {
                    double difference = luminance - Luma(nextRow, x);
                    sum += difference * difference;
                    count++;
                }
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    /// <summary>
    /// Returns the first non-finite (NaN/Inf) RGBA component across the given bitmaps, or null if all finite.
    /// </summary>
    public static string? FirstNonFinite(params (string label, Bitmap bitmap)[] bitmaps)
    {
        foreach ((string label, Bitmap bitmap) in bitmaps)
        {
            EnsureSupported(bitmap, nameof(bitmaps));
            for (int y = 0; y < bitmap.Height; y++)
            {
                ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int offset = x * ChannelCount;
                    for (int channel = 0; channel < ChannelCount; channel++)
                    {
                        float value = HalfBitsToFloat(row[offset + channel]);
                        if (!float.IsFinite(value))
                            return $"{label} (x={x}, y={y}, c={channel}) = {value}";
                    }
                }
            }
        }

        return null;
    }

    private static double WindowSsim(Bitmap a, Bitmap b, int left, int top, int right, int bottom)
    {
        int count = (right - left) * (bottom - top);
        double meanA = 0;
        double meanB = 0;
        for (int y = top; y < bottom; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = left; x < right; x++)
            {
                meanA += Luma(rowA, x);
                meanB += Luma(rowB, x);
            }
        }

        meanA /= count;
        meanB /= count;

        double varianceA = 0;
        double varianceB = 0;
        double covariance = 0;
        for (int y = top; y < bottom; y++)
        {
            ReadOnlySpan<ushort> rowA = a.GetRow<ushort>(y);
            ReadOnlySpan<ushort> rowB = b.GetRow<ushort>(y);
            for (int x = left; x < right; x++)
            {
                double deltaA = Luma(rowA, x) - meanA;
                double deltaB = Luma(rowB, x) - meanB;
                varianceA += deltaA * deltaA;
                varianceB += deltaB * deltaB;
                covariance += deltaA * deltaB;
            }
        }

        varianceA /= count;
        varianceB /= count;
        covariance /= count;

        return Ssim(meanA, meanB, varianceA, varianceB, covariance);
    }

    private static double Ssim(
        double meanA,
        double meanB,
        double varianceA,
        double varianceB,
        double covariance)
    {
        const double c1 = 0.01 * 0.01;
        const double c2 = 0.03 * 0.03;
        double numerator = (2 * meanA * meanB + c1) * (2 * covariance + c2);
        double denominator = (meanA * meanA + meanB * meanB + c1) * (varianceA + varianceB + c2);
        return numerator / denominator;
    }

    private static bool IsInCoverageBand(
        ReadOnlySpan<ushort> referenceRow,
        int offset,
        float minimumCoverage,
        float maximumCoverage)
    {
        float alpha = HalfBitsToFloat(referenceRow[offset + 3]);
        return alpha > minimumCoverage && alpha < maximumCoverage;
    }

    private static void ValidateCoverageBounds(float minimumCoverage, float maximumCoverage)
    {
        if (!float.IsFinite(minimumCoverage) || minimumCoverage < 0 || minimumCoverage >= 1)
            throw new ArgumentOutOfRangeException(nameof(minimumCoverage));
        if (!float.IsFinite(maximumCoverage) || maximumCoverage <= 0 || maximumCoverage > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCoverage));
        if (minimumCoverage >= maximumCoverage)
            throw new ArgumentException("Minimum coverage must be less than maximum coverage.");
    }

    private static double Luma(ReadOnlySpan<ushort> row, int x)
    {
        int offset = x * ChannelCount;
        return LumaR * HalfBitsToFloat(row[offset])
               + LumaG * HalfBitsToFloat(row[offset + 1])
               + LumaB * HalfBitsToFloat(row[offset + 2]);
    }

    private static double AbsoluteError(ushort a, ushort b)
        => Math.Abs(HalfBitsToFloat(a) - HalfBitsToFloat(b));

    private static float HalfBitsToFloat(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);

    private static void EnsureComparable(Bitmap a, Bitmap b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException($"Bitmap sizes differ: {a.Width}x{a.Height} vs {b.Width}x{b.Height}.");

        EnsureSupported(a, nameof(a));
        EnsureSupported(b, nameof(b));
    }

    private static void EnsureSupported(Bitmap bitmap, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.ColorType != BitmapColorType.RgbaF16
            || bitmap.AlphaType != BitmapAlphaType.Premul
            || bitmap.ColorSpace != BitmapColorSpace.LinearSrgb)
        {
            throw new ArgumentException(
                "ImageMetrics expects linear-sRGB, premultiplied RgbaF16 bitmaps.",
                parameterName);
        }
    }
}
