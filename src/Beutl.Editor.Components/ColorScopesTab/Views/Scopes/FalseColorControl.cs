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

        WriteableBitmap result =
            existing?.PixelSize.Width == sourceWidth && existing.PixelSize.Height == sourceHeight
                ? existing
                : new WriteableBitmap(
                    new PixelSize(sourceWidth, sourceHeight),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);

        bool linear = ColorSpace == ScopeColorSpace.Linear;
        BitmapColorSpace targetColorSpace = linear ? BitmapColorSpace.LinearSrgb : BitmapColorSpace.Srgb;
        float invHdr = 1f / MathF.Max(HdrRange, 1e-6f);

        BtlBitmap rgbaF16;
        bool requireDispose = false;
        if (source.ColorType == BitmapColorType.RgbaF16 && source.ColorSpace == targetColorSpace)
        {
            rgbaF16 = source;
        }
        else
        {
            rgbaF16 = source.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Unpremul, targetColorSpace);
            requireDispose = true;
        }

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
                    RgbaF16 pixel = srcRow[x];
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

                    float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float yNorm = Math.Clamp(luma * invHdr, 0f, 1f);

                    (float fr, float fg, float fb) = FalseColorRamp(yNorm);

                    int idx = x * 4;
                    destRow[idx + 0] = (byte)(Math.Clamp(fb, 0f, 1f) * 255f);
                    destRow[idx + 1] = (byte)(Math.Clamp(fg, 0f, 1f) * 255f);
                    destRow[idx + 2] = (byte)(Math.Clamp(fr, 0f, 1f) * 255f);
                    destRow[idx + 3] = 255;
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
