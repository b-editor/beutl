using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beutl.Views.Tools.Scopes;

/// <summary>
/// Control that displays a histogram visualization of RGB color distribution.
/// </summary>
public class HistogramControl : ScopeControlBase
{
    public static readonly DirectProperty<HistogramControl, HistogramMode> ModeProperty =
        AvaloniaProperty.RegisterDirect<HistogramControl, HistogramMode>(
            nameof(Mode), o => o.Mode, (o, v) => o.Mode = v, HistogramMode.Overlay);

    private HistogramMode _mode = HistogramMode.Overlay;

    private static readonly string[] s_horizontalLabels = ["0", "64", "128", "192", "255"];
    private static readonly (float R, float G, float B) s_colorRed = (1.00f, 0.25f, 0.25f);
    private static readonly (float R, float G, float B) s_colorGreen = (0.25f, 1.00f, 0.35f);
    private static readonly (float R, float G, float B) s_colorBlue = (0.35f, 0.60f, 1.00f);

    static HistogramControl()
    {
        AffectsRender<HistogramControl>(ModeProperty);
    }

    public HistogramMode Mode
    {
        get => _mode;
        set => SetAndRaise(ModeProperty, ref _mode, value);
    }

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

        var mode = Mode;

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
        dest.Fill(PackColor(0, 0, 0, 0));
        int stridePixels = fb.RowBytes / sizeof(uint);

        if (mode == HistogramMode.Overlay)
        {
            RenderOverlayMode(rHist, gHist, bHist, dest, stridePixels, targetWidth, targetHeight);
        }
        else
        {
            RenderParadeMode(rHist, gHist, bHist, dest, stridePixels, targetWidth, targetHeight);
        }

        return bitmap;
    }

    private void RenderOverlayMode(int[] rHist, int[] gHist, int[] bHist, Span<uint> dest, int stridePixels, int targetWidth, int targetHeight)
    {
        int rMax = rHist.Max();
        int gMax = gHist.Max();
        int bMax = bHist.Max();
        int max = Math.Max(rMax, Math.Max(gMax, bMax));
        max = Math.Max(max, 1);

        // Pre-compute colors (DaVinci Resolve style)
        uint redColor = PackColor(
            (byte)(s_colorRed.R * 200),
            (byte)(s_colorRed.G * 200),
            (byte)(s_colorRed.B * 200),
            180);
        uint greenColor = PackColor(
            (byte)(s_colorGreen.R * 200),
            (byte)(s_colorGreen.G * 200),
            (byte)(s_colorGreen.B * 200),
            180);
        uint blueColor = PackColor(
            (byte)(s_colorBlue.R * 200),
            (byte)(s_colorBlue.G * 200),
            (byte)(s_colorBlue.B * 200),
            180);

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
    }

    private void RenderParadeMode(int[] rHist, int[] gHist, int[] bHist, Span<uint> dest, int stridePixels, int targetWidth, int targetHeight)
    {
        // In Parade mode, split the height into 3 equal sections: R (top), G (middle), B (bottom)
        int sectionHeight = targetHeight / 3;
        int rSectionStart = 0;
        int gSectionStart = sectionHeight;
        int bSectionStart = sectionHeight * 2;

        int rMax = Math.Max(1, rHist.Max());
        int gMax = Math.Max(1, gHist.Max());
        int bMax = Math.Max(1, bHist.Max());

        // Pre-compute colors (DaVinci Resolve style)
        uint redColor = PackColor(
            (byte)(s_colorRed.R * 220),
            (byte)(s_colorRed.G * 220),
            (byte)(s_colorRed.B * 220),
            200);
        uint greenColor = PackColor(
            (byte)(s_colorGreen.R * 220),
            (byte)(s_colorGreen.G * 220),
            (byte)(s_colorGreen.B * 220),
            200);
        uint blueColor = PackColor(
            (byte)(s_colorBlue.R * 220),
            (byte)(s_colorBlue.G * 220),
            (byte)(s_colorBlue.B * 220),
            200);

        // Render each channel in its section
        for (int i = 0; i < 256; i++)
        {
            int x = i * targetWidth / 256;
            int nextX = (i + 1) * targetWidth / 256;
            int barWidth = Math.Max(1, nextX - x);

            int rHeight = rHist[i] * sectionHeight / rMax;
            int gHeight = gHist[i] * sectionHeight / gMax;
            int bHeight = bHist[i] * sectionHeight / bMax;

            for (int bx = x; bx < x + barWidth && bx < stridePixels; bx++)
            {
                // Red section (top) - draws from bottom of section upward
                for (int y = 0; y < rHeight && y < sectionHeight; y++)
                {
                    int destY = rSectionStart + sectionHeight - 1 - y;
                    if (destY < 0 || destY >= targetHeight) continue;

                    int destIndex = destY * stridePixels + bx;
                    if (destIndex >= dest.Length) continue;

                    dest[destIndex] = BlendAdd(dest[destIndex], redColor);
                }

                // Green section (middle) - draws from bottom of section upward
                for (int y = 0; y < gHeight && y < sectionHeight; y++)
                {
                    int destY = gSectionStart + sectionHeight - 1 - y;
                    if (destY < 0 || destY >= targetHeight) continue;

                    int destIndex = destY * stridePixels + bx;
                    if (destIndex >= dest.Length) continue;

                    dest[destIndex] = BlendAdd(dest[destIndex], greenColor);
                }

                // Blue section (bottom) - draws from bottom of section upward
                for (int y = 0; y < bHeight && y < sectionHeight; y++)
                {
                    int destY = bSectionStart + sectionHeight - 1 - y;
                    if (destY < 0 || destY >= targetHeight) continue;

                    int destIndex = destY * stridePixels + bx;
                    if (destIndex >= dest.Length) continue;

                    dest[destIndex] = BlendAdd(dest[destIndex], blueColor);
                }
            }
        }
    }
}
