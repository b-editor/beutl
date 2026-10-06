using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Media;
using Beutl.Media.Pixel;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

/// <summary>
/// Renders the source frame as a 9-band false-color (thermal) exposure map.
/// </summary>
public sealed class FalseColorControl : ImageOverlayScopeBase
{
    static FalseColorControl()
    {
    }

    protected override Orientation DragAxis => Orientation.Vertical;

    protected override unsafe WriteableBitmap? RenderImage(BtlBitmap source, WriteableBitmap? existing)
    {
        int sourceWidth = source.Width;
        int sourceHeight = source.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return existing;

        WriteableBitmap result = ScopeBitmaps.ReuseOrCreate(existing, sourceWidth, sourceHeight);

        BitmapColorSpace targetColorSpace = ScopeBitmaps.ToBitmapColorSpace(ColorSpace);
        float invHdr = 1f / MathF.Max(HdrRange, 1e-6f);

        BtlBitmap rgbaF16 = ScopeBitmaps.ToRgbaF16(source, targetColorSpace, BitmapAlphaType.Unpremul, out bool requireDispose);

        try
        {
            using ILockedFramebuffer fb = result.Lock();
            byte* destPtr = (byte*)fb.Address;
            int destRowBytes = fb.RowBytes;
            byte* srcData = (byte*)rgbaF16.Data;
            int srcRowBytes = rgbaF16.RowBytes;
            bool premul = rgbaF16.AlphaType == BitmapAlphaType.Premul;

            Parallel.For(0, sourceHeight, y =>
            {
                RgbaF16* srcRow = (RgbaF16*)(srcData + (long)y * srcRowBytes);
                byte* destRow = destPtr + (long)y * destRowBytes;

                for (int x = 0; x < sourceWidth; x++)
                {
                    ScopeBitmaps.ReadUnpremultiplied(in srcRow[x], premul, out float r, out float g, out float b);

                    float luma = ScopeBitmaps.Luma(r, g, b);
                    float yNorm = Math.Clamp(luma * invHdr, 0f, 1f);

                    (float fr, float fg, float fb) = FalseColorRamp(yNorm);

                    ScopeBitmaps.WriteBgra(destRow + x * 4, fr, fg, fb);
                }
            });
        }
        finally
        {
            if (requireDispose)
                rgbaF16.Dispose();
        }

        return result;
    }

    // Mirrors the GPU shader ramp in PlayerView (BitmapView/HdrBitmapView).
    private static (float R, float G, float B) FalseColorRamp(float y)
    {
        if (y >= 0.999f) return (1.0f, 1.0f, 1.0f);
        if (y >= 0.97f) return (1.0f, 0.0f, 0.0f);
        if (y >= 0.84f) return (1.0f, 0.55f, 0.0f);
        if (y >= 0.78f) return (1.0f, 1.0f, 0.0f);
        if (y >= 0.56f) return (0.5f, 0.5f, 0.5f);
        if (y >= 0.52f) return (1.0f, 0.6f, 0.7f);
        if (y >= 0.38f) return (0.0f, 0.85f, 0.0f);
        if (y >= 0.025f) return (0.0f, 0.3f, 1.0f);
        return (0.4f, 0.0f, 0.6f);
    }
}
