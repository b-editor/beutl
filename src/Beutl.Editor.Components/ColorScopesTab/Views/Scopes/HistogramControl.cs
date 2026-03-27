using Avalonia;
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
public class HistogramControl : ScopeControlBase
{
    public static readonly DirectProperty<HistogramControl, HistogramMode> ModeProperty =
        AvaloniaProperty.RegisterDirect<HistogramControl, HistogramMode>(
            nameof(Mode), o => o.Mode, (o, v) => o.Mode = v, HistogramMode.Overlay);

    public static readonly DirectProperty<HistogramControl, float> HdrRangeProperty =
        AvaloniaProperty.RegisterDirect<HistogramControl, float>(
            nameof(HdrRange), o => o.HdrRange, (o, v) => o.HdrRange = v, 1.0f);

    private HistogramMode _mode = HistogramMode.Overlay;
    private float _hdrRange = 1.0f;

    private static readonly string[] s_horizontalLabels = ["0", "64", "128", "192", "255"];
    private static readonly (float R, float G, float B) s_colorRed = (1.00f, 0.25f, 0.25f);
    private static readonly (float R, float G, float B) s_colorGreen = (0.25f, 1.00f, 0.35f);
    private static readonly (float R, float G, float B) s_colorBlue = (0.35f, 0.60f, 1.00f);

    static HistogramControl()
    {
        AffectsRender<HistogramControl>(ModeProperty, HdrRangeProperty);
        ModeProperty.Changed.AddClassHandler<HistogramControl>((o, _) => o.Refresh());
        HdrRangeProperty.Changed.AddClassHandler<HistogramControl>((o, _) => o.Refresh());
    }

    public HistogramMode Mode
    {
        get => _mode;
        set => SetAndRaise(ModeProperty, ref _mode, value);
    }

    public float HdrRange
    {
        get => _hdrRange;
        set => SetAndRaise(HdrRangeProperty, ref _hdrRange, Math.Max(value, 0.01f));
    }

    protected override string[]? VerticalAxisLabels => null;

    protected override string[]? HorizontalAxisLabels =>
        _hdrRange > 1.01f
            ? ["0", $"{_hdrRange * 0.25f:F1}", $"{_hdrRange * 0.5f:F1}", $"{_hdrRange * 0.75f:F1}", $"{_hdrRange:F1}"]
            : s_horizontalLabels;

    protected override unsafe WriteableBitmap RenderScope(
        BtlBitmap sourceBitmap,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap)
    {
        // Calculate histograms using RgbaF16 for HDR support
        const int binCount = 256;
        var rHist = new int[binCount];
        var gHist = new int[binCount];
        var bHist = new int[binCount];

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
            for (int y = 0; y < sourceHeight; y += yStep)
            {
                var row = rgbaF16.GetRow<RgbaF16>(y);
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

                    rHist[Math.Clamp((int)(r * binScale + 0.5f), 0, binCount - 1)]++;
                    gHist[Math.Clamp((int)(g * binScale + 0.5f), 0, binCount - 1)]++;
                    bHist[Math.Clamp((int)(b * binScale + 0.5f), 0, binCount - 1)]++;
                }
            }
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
