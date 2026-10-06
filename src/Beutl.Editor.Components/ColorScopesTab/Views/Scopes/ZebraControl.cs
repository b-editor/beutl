using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Media;
using Beutl.Media.Pixel;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

/// <summary>
/// Renders the source frame with diagonal zebra stripes drawn over pixels whose luma exceeds
/// the high threshold or falls below the low threshold (exposure check).
/// </summary>
public sealed class ZebraControl : ImageOverlayScopeBase
{
    public static readonly DirectProperty<ZebraControl, float> HighThresholdProperty =
        AvaloniaProperty.RegisterDirect<ZebraControl, float>(
            nameof(HighThreshold), o => o.HighThreshold, (o, v) => o.HighThreshold = v, 0.95f);

    public static readonly DirectProperty<ZebraControl, float> LowThresholdProperty =
        AvaloniaProperty.RegisterDirect<ZebraControl, float>(
            nameof(LowThreshold), o => o.LowThreshold, (o, v) => o.LowThreshold = v, 0.03f);

    private float _highThreshold = 0.95f;
    private float _lowThreshold = 0.03f;

    private const int StripePeriod = 8;

    static ZebraControl()
    {
        AffectsRender<ZebraControl>(HighThresholdProperty, LowThresholdProperty);
        HighThresholdProperty.Changed.AddClassHandler<ZebraControl>((o, _) => o.Refresh());
        LowThresholdProperty.Changed.AddClassHandler<ZebraControl>((o, _) => o.Refresh());
    }

    protected override Orientation DragAxis => Orientation.Vertical;

    public float HighThreshold
    {
        get => _highThreshold;
        set => SetAndRaise(HighThresholdProperty, ref _highThreshold, Math.Clamp(value, 0f, 1f));
    }

    public float LowThreshold
    {
        get => _lowThreshold;
        set => SetAndRaise(LowThresholdProperty, ref _lowThreshold, Math.Clamp(value, 0f, 1f));
    }

    protected override unsafe WriteableBitmap? RenderImage(BtlBitmap source, WriteableBitmap? existing)
    {
        int sourceWidth = source.Width;
        int sourceHeight = source.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return existing;

        WriteableBitmap result = ScopeBitmaps.ReuseOrCreate(existing, sourceWidth, sourceHeight);

        // Source for luma calculation (linear or gamma).
        BitmapColorSpace lumaColorSpace = ScopeBitmaps.ToBitmapColorSpace(ColorSpace);
        // Source for display output (always sRGB display).
        BitmapColorSpace displayColorSpace = BitmapColorSpace.Srgb;
        float invHdr = 1f / MathF.Max(HdrRange, 1e-6f);
        float high = _highThreshold;
        float low = _lowThreshold;

        BtlBitmap lumaBitmap = ScopeBitmaps.ToRgbaF16(source, lumaColorSpace, BitmapAlphaType.Unpremul, out bool disposeLuma);
        BtlBitmap displayBitmap = ResolveDisplayBitmap(source, lumaBitmap, displayColorSpace, out bool disposeDisplay);

        try
        {
            using ILockedFramebuffer fb = result.Lock();
            byte* destPtr = (byte*)fb.Address;
            int destRowBytes = fb.RowBytes;

            byte* lumaData = (byte*)lumaBitmap.Data;
            int lumaRowBytes = lumaBitmap.RowBytes;
            bool lumaPremul = lumaBitmap.AlphaType == BitmapAlphaType.Premul;

            byte* displayData = (byte*)displayBitmap.Data;
            int displayRowBytes = displayBitmap.RowBytes;
            bool displayPremul = displayBitmap.AlphaType == BitmapAlphaType.Premul;

            Parallel.For(0, sourceHeight, y =>
            {
                RgbaF16* lumaRow = (RgbaF16*)(lumaData + (long)y * lumaRowBytes);
                RgbaF16* dispRow = (RgbaF16*)(displayData + (long)y * displayRowBytes);
                byte* destRow = destPtr + (long)y * destRowBytes;

                for (int x = 0; x < sourceWidth; x++)
                {
                    ScopeBitmaps.ReadUnpremultiplied(in lumaRow[x], lumaPremul, out float lr, out float lg, out float lb);
                    float luma = ScopeBitmaps.Luma(lr, lg, lb);
                    float yNorm = Math.Clamp(luma * invHdr, 0f, 1f);

                    ScopeBitmaps.ReadUnpremultiplied(in dispRow[x], displayPremul, out float dr, out float dg, out float db);

                    bool over = yNorm >= high;
                    bool under = yNorm <= low;
                    if (over || under)
                    {
                        int phase = ((x + y) % StripePeriod) * 2 < StripePeriod ? 0 : 1;
                        if (over)
                        {
                            // Black/white stripes for over-exposure.
                            float v = phase;
                            dr = v;
                            dg = v;
                            db = v;
                        }
                        else
                        {
                            // Red/black stripes for under-exposure.
                            dr = phase;
                            dg = 0f;
                            db = 0f;
                        }
                    }

                    ScopeBitmaps.WriteBgra(destRow + x * 4, dr, dg, db);
                }
            });
        }
        finally
        {
            if (disposeLuma)
                lumaBitmap.Dispose();
            if (disposeDisplay)
                displayBitmap.Dispose();
        }

        return result;
    }

    // The luma bitmap doubles as the display bitmap when the scope measures in sRGB.
    private static BtlBitmap ResolveDisplayBitmap(
        BtlBitmap source, BtlBitmap lumaBitmap, BitmapColorSpace displayColorSpace, out bool converted)
    {
        converted = false;
        if (source.ColorType == BitmapColorType.RgbaF16 && source.ColorSpace == displayColorSpace)
            return source;

        if (lumaBitmap.ColorSpace == displayColorSpace && lumaBitmap.ColorType == BitmapColorType.RgbaF16)
            return lumaBitmap;

        BtlBitmap displayBitmap = source.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, displayColorSpace);
        converted = true;
        return displayBitmap;
    }
}
