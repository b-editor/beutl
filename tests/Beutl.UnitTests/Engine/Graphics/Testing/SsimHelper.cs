using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Testing;

/// <summary>
/// SSIM (Structural Similarity Index) helper for comparing two same-size bitmaps.
/// Used by <c>ResolutionEquivalenceTests</c> to verify that proxy-rendered output matches the
/// full-resolution reference after the compositor's upscale blit, per SC-001 (SSIM ≥ 0.97).
/// </summary>
internal static class SsimHelper
{
    private const int WindowSize = 8;
    private const double DynamicRange = 255.0;
    private const double K1 = 0.01;
    private const double K2 = 0.03;
    private static readonly double s_c1 = (K1 * DynamicRange) * (K1 * DynamicRange);
    private static readonly double s_c2 = (K2 * DynamicRange) * (K2 * DynamicRange);

    /// <summary>
    /// Computes the mean SSIM between two bitmaps (R/G/B channels averaged; alpha ignored).
    /// Both bitmaps must have the same dimensions and BGRA 8888 layout.
    /// </summary>
    /// <returns>A value in <c>[-1, 1]</c>; <c>1.0</c> is identical, typical "visually identical" threshold is <c>≥ 0.97</c>.</returns>
    public static double Compute(Bitmap reference, Bitmap candidate)
    {
        if (reference.Width != candidate.Width || reference.Height != candidate.Height)
            throw new ArgumentException(
                $"Bitmap dimensions differ: reference={reference.Width}×{reference.Height}, candidate={candidate.Width}×{candidate.Height}");

        // RenderTarget.Snapshot may return float16 / 16-bit-per-channel formats depending on the
        // backend; the windowed SSIM math assumes 8-bit channels in the standard L=255 range.
        // Convert both to BGRA 8888 (cheap when already 8-bit) and dispose the conversions after use.
        Bitmap refBmp = reference.BytesPerPixel == 4 ? reference : reference.Convert(BitmapColorType.Bgra8888);
        Bitmap candBmp = candidate.BytesPerPixel == 4 ? candidate : candidate.Convert(BitmapColorType.Bgra8888);
        try
        {
            return ComputeBgra8888(refBmp, candBmp);
        }
        finally
        {
            if (!ReferenceEquals(refBmp, reference)) refBmp.Dispose();
            if (!ReferenceEquals(candBmp, candidate)) candBmp.Dispose();
        }
    }

    private static double ComputeBgra8888(Bitmap reference, Bitmap candidate)
    {
        int width = reference.Width;
        int height = reference.Height;
        int windowsX = width / WindowSize;
        int windowsY = height / WindowSize;
        if (windowsX == 0 || windowsY == 0)
            throw new ArgumentException($"Bitmap too small for {WindowSize}×{WindowSize} windowed SSIM ({width}×{height}).");

        Span<byte> refSpan = reference.GetPixelSpan();
        Span<byte> candSpan = candidate.GetPixelSpan();
        int rowBytesRef = reference.RowBytes;
        int rowBytesCand = candidate.RowBytes;

        double sum = 0;
        int count = 0;
        for (int wy = 0; wy < windowsY; wy++)
        {
            for (int wx = 0; wx < windowsX; wx++)
            {
                int x0 = wx * WindowSize;
                int y0 = wy * WindowSize;
                double channelSum = 0;
                // Iterate B, G, R channels (skip A at byte offset 3 for BGRA / 3 for RGBA — both are alpha at offset 3 for 8888 4-byte formats).
                for (int channel = 0; channel < 3; channel++)
                {
                    channelSum += ComputeChannelWindow(refSpan, candSpan, rowBytesRef, rowBytesCand, x0, y0, channel);
                }
                sum += channelSum / 3.0;
                count++;
            }
        }

        return sum / count;
    }

    private static double ComputeChannelWindow(
        ReadOnlySpan<byte> refSpan, ReadOnlySpan<byte> candSpan,
        int rowBytesRef, int rowBytesCand, int x0, int y0, int channel)
    {
        int n = WindowSize * WindowSize;
        double sumR = 0, sumC = 0;
        for (int y = 0; y < WindowSize; y++)
        {
            int rowOffR = (y0 + y) * rowBytesRef;
            int rowOffC = (y0 + y) * rowBytesCand;
            for (int x = 0; x < WindowSize; x++)
            {
                int idxR = rowOffR + (x0 + x) * 4 + channel;
                int idxC = rowOffC + (x0 + x) * 4 + channel;
                sumR += refSpan[idxR];
                sumC += candSpan[idxC];
            }
        }
        double muR = sumR / n;
        double muC = sumC / n;

        double sigmaRSq = 0, sigmaCSq = 0, sigmaRC = 0;
        for (int y = 0; y < WindowSize; y++)
        {
            int rowOffR = (y0 + y) * rowBytesRef;
            int rowOffC = (y0 + y) * rowBytesCand;
            for (int x = 0; x < WindowSize; x++)
            {
                int idxR = rowOffR + (x0 + x) * 4 + channel;
                int idxC = rowOffC + (x0 + x) * 4 + channel;
                double dr = refSpan[idxR] - muR;
                double dc = candSpan[idxC] - muC;
                sigmaRSq += dr * dr;
                sigmaCSq += dc * dc;
                sigmaRC += dr * dc;
            }
        }
        sigmaRSq /= n - 1;
        sigmaCSq /= n - 1;
        sigmaRC /= n - 1;

        double numerator = (2 * muR * muC + s_c1) * (2 * sigmaRC + s_c2);
        double denominator = (muR * muR + muC * muC + s_c1) * (sigmaRSq + sigmaCSq + s_c2);
        return denominator == 0 ? 1.0 : numerator / denominator;
    }
}
