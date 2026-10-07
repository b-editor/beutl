using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Media;
using Beutl.Media.Pixel;
using BtlBitmap = Beutl.Media.Bitmap;
using PixelSize = Avalonia.PixelSize;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

internal static class ScopeBitmaps
{
    public static BitmapColorSpace ToBitmapColorSpace(ScopeColorSpace colorSpace)
    {
        return colorSpace == ScopeColorSpace.Linear
            ? BitmapColorSpace.LinearSrgb
            : BitmapColorSpace.Srgb;
    }

    // The previous result is drawn into again while it still has the requested size.
    public static WriteableBitmap ReuseOrCreate(WriteableBitmap? existing, int width, int height)
    {
        return existing?.PixelSize.Width == width && existing.PixelSize.Height == height
            ? existing
            : new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
    }

    // Scopes read RgbaF16 pixels in the color space they measure; a source already in that form is
    // read in place, and only a converted copy is the caller's to dispose.
    public static BtlBitmap ToRgbaF16(
        BtlBitmap source, BitmapColorSpace colorSpace, BitmapAlphaType? alphaType, out bool converted)
    {
        if (source.ColorType == BitmapColorType.RgbaF16 && source.ColorSpace == colorSpace)
        {
            converted = false;
            return source;
        }

        BtlBitmap result = source.Convert(BitmapColorType.RgbaF16, alphaType, colorSpace);
        converted = true;
        return result;
    }

    // Per-sample helpers for the scope inner loops; they keep the exact float expressions of the loops they
    // replace, so the rendered bytes do not change. Only a fractional alpha is divided out: a zero alpha has
    // no colour left to recover and an alpha of one or more is left as is.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadUnpremultiplied(in RgbaF16 pixel, bool premul, out float r, out float g, out float b)
    {
        r = (float)pixel.R;
        g = (float)pixel.G;
        b = (float)pixel.B;

        if (premul)
        {
            float a = (float)pixel.A;
            if (a > 0f && a < 1f)
            {
                float invA = 1f / a;
                r *= invA;
                g *= invA;
                b *= invA;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Luma(float r, float g, float b)
    {
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    // Writes one opaque Bgra8888 pixel.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void WriteBgra(byte* dest, float r, float g, float b)
    {
        dest[0] = (byte)(Math.Clamp(b, 0f, 1f) * 255f);
        dest[1] = (byte)(Math.Clamp(g, 0f, 1f) * 255f);
        dest[2] = (byte)(Math.Clamp(r, 0f, 1f) * 255f);
        dest[3] = 255;
    }
}
