using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Media;
using Beutl.Media.Pixel;
using BtlBitmap = Beutl.Media.Bitmap;
using PixelSize = Avalonia.PixelSize;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

/// <summary>
/// Control that displays a histogram visualization of RGB color distribution.
/// </summary>
public class HistogramControl : HdrScopeControlBase
{
    public static readonly DirectProperty<HistogramControl, HistogramMode> ModeProperty =
        AvaloniaProperty.RegisterDirect<HistogramControl, HistogramMode>(
            nameof(Mode), o => o.Mode, (o, v) => o.Mode = v, HistogramMode.Overlay);

    private HistogramMode _mode = HistogramMode.Overlay;

    // Cached histogram bin arrays (reused across frames to avoid per-frame allocation)
    // Thread safety: _renderLock in ScopeControlBase serializes RenderScope calls
    private readonly int[] _rHist = new int[256];
    private readonly int[] _gHist = new int[256];
    private readonly int[] _bHist = new int[256];
    private readonly object _histLock = new();

    private static readonly string[] s_horizontalLabelsSdr = ["0", "0.3", "0.5", "0.8", "1.0"];
    private static readonly (float R, float G, float B) s_colorRed = (1.00f, 0.25f, 0.25f);
    private static readonly (float R, float G, float B) s_colorGreen = (0.25f, 1.00f, 0.35f);
    private static readonly (float R, float G, float B) s_colorBlue = (0.35f, 0.60f, 1.00f);

    static HistogramControl()
    {
        AffectsRender<HistogramControl>(ModeProperty);
        ModeProperty.Changed.AddClassHandler<HistogramControl>((o, _) => o.Refresh());
    }

    protected override Orientation DragAxis => Orientation.Horizontal;

    public HistogramMode Mode
    {
        get => _mode;
        set => SetAndRaise(ModeProperty, ref _mode, value);
    }

    protected override string[]? VerticalAxisLabels => null;

    protected override string[]? HorizontalAxisLabels =>
        HdrRange is > 0.99f and < 1.01f
            ? s_horizontalLabelsSdr
            : ["0", $"{HdrRange * 0.25f:F1}", $"{HdrRange * 0.5f:F1}", $"{HdrRange * 0.75f:F1}", $"{HdrRange:F1}"];

    protected override unsafe WriteableBitmap RenderScope(
        BtlBitmap sourceBitmap,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap)
    {
        const int binCount = 256;
        int[] rHist = _rHist;
        int[] gHist = _gHist;
        int[] bHist = _bHist;
        Array.Clear(rHist);
        Array.Clear(gHist);
        Array.Clear(bHist);

        int sourceWidth = sourceBitmap.Width;
        int sourceHeight = sourceBitmap.Height;
        int xStep = Math.Max(1, sourceWidth / 256);
        int yStep = Math.Max(1, sourceHeight / 256);
        float hdrRange = HdrRange;
        float binScale = (binCount - 1) / hdrRange;

        BtlBitmap rgbaF16;
        bool requireDispose = false;
        if (sourceBitmap.ColorType == BitmapColorType.RgbaF16 && sourceBitmap.ColorSpace == BitmapColorSpace.Srgb)
        {
            rgbaF16 = sourceBitmap;
        }
        else
        {
            rgbaF16 = sourceBitmap.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            requireDispose = true;
        }

        try
        {
            bool premul = rgbaF16.AlphaType == BitmapAlphaType.Premul;
            int yCount = (sourceHeight + yStep - 1) / yStep;

            // Parallelize binning with per-task local histograms, merged at the end
            var rgbaF16Local = rgbaF16; // capture for closure
            Parallel.For(0, yCount,
                () => (new int[binCount], new int[binCount], new int[binCount]),
                (yi, _, local) =>
                {
                    int y = yi * yStep;
                    var (lR, lG, lB) = local;
                    // ReSharper disable once AccessToDisposedClosure
                    var row = rgbaF16Local.GetRow<RgbaF16>(y);
                    for (int x = 0; x < sourceWidth; x += xStep)
                    {
                        RgbaF16 pixel = row[x];
                        float r = (float)pixel.R;
                        float g = (float)pixel.G;
                        float b = (float)pixel.B;
                        float a = (float)pixel.A;

                        if (premul && a > 0f && a < 1f)
                        {
                            float invA = 1f / a;
                            r *= invA;
                            g *= invA;
                            b *= invA;
                        }

                        lR[Math.Clamp((int)(r * binScale + 0.5f), 0, binCount - 1)]++;
                        lG[Math.Clamp((int)(g * binScale + 0.5f), 0, binCount - 1)]++;
                        lB[Math.Clamp((int)(b * binScale + 0.5f), 0, binCount - 1)]++;
                    }
                    return local;
                },
                local =>
                {
                    var (lR, lG, lB) = local;
                    lock (_histLock)
                    {
                        for (int i = 0; i < binCount; i++)
                        {
                            rHist[i] += lR[i];
                            gHist[i] += lG[i];
                            bHist[i] += lB[i];
                        }
                    }
                });
        }
        finally
        {
            if (requireDispose)
                rgbaF16.Dispose();
        }
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

    private static (int rMax, int gMax, int bMax) ComputeMax(int[] rHist, int[] gHist, int[] bHist)
    {
        int rMax = 0, gMax = 0, bMax = 0;
        for (int i = 0; i < 256; i++)
        {
            if (rHist[i] > rMax) rMax = rHist[i];
            if (gHist[i] > gMax) gMax = gHist[i];
            if (bHist[i] > bMax) bMax = bHist[i];
        }
        return (rMax, gMax, bMax);
    }

    private void RenderOverlayMode(int[] rHist, int[] gHist, int[] bHist, Span<uint> dest, int stridePixels, int targetWidth, int targetHeight)
    {
        var (rMax, gMax, bMax) = ComputeMax(rHist, gHist, bHist);
        int max = Math.Max(1, Math.Max(rMax, Math.Max(gMax, bMax)));

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

        // Render histogram bars with per-bin pre-computed zone colors (no per-pixel branching/blending)
        for (int i = 0; i < 256; i++)
        {
            int x = i * targetWidth / 256;
            int nextX = (i + 1) * targetWidth / 256;
            int barWidth = Math.Max(1, nextX - x);
            int barEnd = Math.Min(x + barWidth, stridePixels);

            // Sort (height, color) triples ascending by height
            (int H, uint C) a = (rHist[i] * targetHeight / max, redColor);
            (int H, uint C) b = (gHist[i] * targetHeight / max, greenColor);
            (int H, uint C) c = (bHist[i] * targetHeight / max, blueColor);
            if (a.H > b.H) (a, b) = (b, a);
            if (b.H > c.H) (b, c) = (c, b);
            if (a.H > b.H) (a, b) = (b, a);
            // Now a.H <= b.H <= c.H

            // Zones: [0, a.H) = a+b+c, [a.H, b.H) = b+c, [b.H, c.H) = c
            uint zone0 = BlendAdd(BlendAdd(BlendAdd(0u, a.C), b.C), c.C);
            uint zone1 = BlendAdd(BlendAdd(0u, b.C), c.C);
            uint zone2 = BlendAdd(0u, c.C);

            // Clamp to target bounds
            int h0 = Math.Min(a.H, targetHeight);
            int h1 = Math.Min(b.H, targetHeight);
            int h2 = Math.Min(c.H, targetHeight);

            for (int bx = x; bx < barEnd; bx++)
            {
                // Zone 0 (all 3 channels): rows [targetHeight - h0, targetHeight)
                for (int y = 0; y < h0; y++)
                    dest[(targetHeight - 1 - y) * stridePixels + bx] = zone0;
                // Zone 1 (2 channels): rows [targetHeight - h1, targetHeight - h0)
                for (int y = h0; y < h1; y++)
                    dest[(targetHeight - 1 - y) * stridePixels + bx] = zone1;
                // Zone 2 (1 channel): rows [targetHeight - h2, targetHeight - h1)
                for (int y = h1; y < h2; y++)
                    dest[(targetHeight - 1 - y) * stridePixels + bx] = zone2;
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

        var (rMaxR, gMaxR, bMaxR) = ComputeMax(rHist, gHist, bHist);
        int rMax = Math.Max(1, rMaxR);
        int gMax = Math.Max(1, gMaxR);
        int bMax = Math.Max(1, bMaxR);

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

        // Pre-blend once (each bar writes to a unique pixel within its section)
        uint redBlended = BlendAdd(0u, redColor);
        uint greenBlended = BlendAdd(0u, greenColor);
        uint blueBlended = BlendAdd(0u, blueColor);

        // Render each channel in its section
        for (int i = 0; i < 256; i++)
        {
            int x = i * targetWidth / 256;
            int nextX = (i + 1) * targetWidth / 256;
            int barWidth = Math.Max(1, nextX - x);
            int barEnd = Math.Min(x + barWidth, stridePixels);

            int rHeight = Math.Min(rHist[i] * sectionHeight / rMax, sectionHeight);
            int gHeight = Math.Min(gHist[i] * sectionHeight / gMax, sectionHeight);
            int bHeight = Math.Min(bHist[i] * sectionHeight / bMax, sectionHeight);

            for (int bx = x; bx < barEnd; bx++)
            {
                // Red section (top) - fill from bottom of section upward
                for (int y = 0; y < rHeight; y++)
                {
                    int destY = rSectionStart + sectionHeight - 1 - y;
                    if ((uint)destY < (uint)targetHeight)
                        dest[destY * stridePixels + bx] = redBlended;
                }
                // Green section (middle)
                for (int y = 0; y < gHeight; y++)
                {
                    int destY = gSectionStart + sectionHeight - 1 - y;
                    if ((uint)destY < (uint)targetHeight)
                        dest[destY * stridePixels + bx] = greenBlended;
                }
                // Blue section (bottom)
                for (int y = 0; y < bHeight; y++)
                {
                    int destY = bSectionStart + sectionHeight - 1 - y;
                    if ((uint)destY < (uint)targetHeight)
                        dest[destY * stridePixels + bx] = blueBlended;
                }
            }
        }
    }
}
