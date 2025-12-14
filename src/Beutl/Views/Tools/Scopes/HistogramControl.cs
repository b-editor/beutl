using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beutl.Views.Tools.Scopes;

/// <summary>
/// Control that displays a histogram visualization of RGB color distribution.
/// </summary>
public class HistogramControl : ScopeControlBase
{
    private static readonly string[] s_horizontalLabels = ["0", "64", "128", "192", "255"];

    protected override string[]? VerticalAxisLabels => null;

    protected override string[]? HorizontalAxisLabels => s_horizontalLabels;

    protected override unsafe WriteableBitmap RenderScope(
        byte[] sourceData,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap,
        CancellationToken token)
    {
        // Calculate histograms
        var rHist = new int[256];
        var gHist = new int[256];
        var bHist = new int[256];

        int xStep = Math.Max(1, sourceWidth / 256);
        int yStep = Math.Max(1, sourceHeight / 256);

        for (int y = 0; y < sourceHeight; y += yStep)
        {
            if ((y & 0x3F) == 0)
                token.ThrowIfCancellationRequested();

            int row = y * sourceStride;
            for (int x = 0; x < sourceWidth; x += xStep)
            {
                int idx = row + x * 4;
                bHist[sourceData[idx]]++;
                gHist[sourceData[idx + 1]]++;
                rHist[sourceData[idx + 2]]++;
            }
        }

        token.ThrowIfCancellationRequested();

        int rMax = rHist.Max();
        int gMax = gHist.Max();
        int bMax = bHist.Max();
        int max = Math.Max(rMax, Math.Max(gMax, bMax));
        max = Math.Max(max, 1);

        // Reuse existing bitmap if size matches
        WriteableBitmap bitmap = existingBitmap?.PixelSize.Width == targetWidth && existingBitmap.PixelSize.Height == targetHeight
            ? existingBitmap
            : new WriteableBitmap(
                new PixelSize(targetWidth, targetHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

        using ILockedFramebuffer fb = bitmap.Lock();
        var dest = new Span<uint>((void*)fb.Address, (fb.RowBytes * fb.Size.Height) / sizeof(uint));
        dest.Fill(PackColor(10, 10, 10));
        int stridePixels = fb.RowBytes / sizeof(uint);

        // Pre-compute colors
        uint redColor = PackColor(200, 80, 80, 180);
        uint greenColor = PackColor(80, 200, 120, 180);
        uint blueColor = PackColor(80, 140, 220, 180);

        // Render histogram bars
        for (int i = 0; i < 256; i++)
        {
            int x = i * targetWidth / 256;
            int nextX = (i + 1) * targetWidth / 256;
            int barWidth = Math.Max(1, nextX - x);

            int rHeight = rHist[i] * targetHeight / max;
            int gHeight = gHist[i] * targetHeight / max;
            int bHeight = bHist[i] * targetHeight / max;

            int maxHeight = Math.Max(rHeight, Math.Max(gHeight, bHeight));

            for (int bx = x; bx < x + barWidth && bx < stridePixels; bx++)
            {
                for (int y = 0; y < maxHeight && y < targetHeight; y++)
                {
                    int destY = targetHeight - 1 - y;
                    if (destY < 0) continue;

                    int destIndex = destY * stridePixels + bx;
                    if (destIndex >= dest.Length) continue;

                    uint color = dest[destIndex];

                    if (y < rHeight)
                        color = BlendAdd(color, redColor);
                    if (y < gHeight)
                        color = BlendAdd(color, greenColor);
                    if (y < bHeight)
                        color = BlendAdd(color, blueColor);

                    dest[destIndex] = color;
                }
            }
        }

        return bitmap;
    }
}
