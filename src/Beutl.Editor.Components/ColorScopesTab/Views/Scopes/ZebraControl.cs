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

        WriteableBitmap result =
            existing?.PixelSize.Width == sourceWidth && existing.PixelSize.Height == sourceHeight
                ? existing
                : new WriteableBitmap(
                    new PixelSize(sourceWidth, sourceHeight),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);

        bool linear = ColorSpace == ScopeColorSpace.Linear;
        // Source for luma calculation (linear or gamma).
        BitmapColorSpace lumaColorSpace = linear ? BitmapColorSpace.LinearSrgb : BitmapColorSpace.Srgb;
        // Source for display output (always sRGB display).
        BitmapColorSpace displayColorSpace = BitmapColorSpace.Srgb;
        float invHdr = 1f / MathF.Max(HdrRange, 1e-6f);
        float high = _highThreshold;
        float low = _lowThreshold;

        BtlBitmap lumaBitmap;
        bool disposeLuma = false;
        if (source.ColorType == BitmapColorType.RgbaF16 && source.ColorSpace == lumaColorSpace)
        {
            lumaBitmap = source;
        }
        else
        {
            lumaBitmap = source.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, lumaColorSpace);
            disposeLuma = true;
        }

        BtlBitmap displayBitmap;
        bool disposeDisplay = false;
        if (source.ColorType == BitmapColorType.RgbaF16 && source.ColorSpace == displayColorSpace)
        {
            displayBitmap = source;
        }
        else if (lumaBitmap.ColorSpace == displayColorSpace && lumaBitmap.ColorType == BitmapColorType.RgbaF16)
        {
            displayBitmap = lumaBitmap;
        }
        else
        {
            displayBitmap = source.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, displayColorSpace);
            disposeDisplay = true;
        }

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
                    RgbaF16 lumaPixel = lumaRow[x];
                    float lr = (float)lumaPixel.R;
                    float lg = (float)lumaPixel.G;
                    float lb = (float)lumaPixel.B;
                    float la = (float)lumaPixel.A;
                    if (lumaPremul && la > 0f && la < 1f)
                    {
                        float invA = 1f / la;
                        lr *= invA;
                        lg *= invA;
                        lb *= invA;
                    }
                    float luma = 0.2126f * lr + 0.7152f * lg + 0.0722f * lb;
                    float yNorm = Math.Clamp(luma * invHdr, 0f, 1f);

                    RgbaF16 dispPixel = dispRow[x];
                    float dr = (float)dispPixel.R;
                    float dg = (float)dispPixel.G;
                    float db = (float)dispPixel.B;
                    float da = (float)dispPixel.A;
                    if (displayPremul && da > 0f && da < 1f)
                    {
                        float invA = 1f / da;
                        dr *= invA;
                        dg *= invA;
                        db *= invA;
                    }

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

                    int idx = x * 4;
                    destRow[idx + 0] = (byte)(Math.Clamp(db, 0f, 1f) * 255f);
                    destRow[idx + 1] = (byte)(Math.Clamp(dg, 0f, 1f) * 255f);
                    destRow[idx + 2] = (byte)(Math.Clamp(dr, 0f, 1f) * 255f);
                    destRow[idx + 3] = 255;
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
}
