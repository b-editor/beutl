using System.Runtime.CompilerServices;

namespace Beutl.Media;

public static class YuvConversion
{
    // Existing callers default to BT.601 coefficients (same as OpenCV).

    public static unsafe void BgraToI420(byte* src, int srcStride, byte* dst, int width, int height)
        => BgraToI420(src, srcStride, dst, width, height, bt709: false);

    public static unsafe void BgraToI420(byte* src, int srcStride, byte* dst, int width, int height, bool bt709)
    {
        // Full-range R'G'B' to limited-range YUV; transfer/gamut conversion belongs to the caller.
        (int yr, int yg, int yb, int ur, int ug, int vr, int vg, int vb) = bt709
            ? (47, 157, 16, -26, -86, 112, -102, -10)
            : (66, 129, 25, -38, -74, 112, -94, -18);
        int yPlaneSize = width * height;
        int uvWidth = (width + 1) / 2;
        int uvHeight = (height + 1) / 2;
        int uPlaneOffset = yPlaneSize;
        int vPlaneOffset = yPlaneSize + uvWidth * uvHeight;

        nint srcAddr = (nint)src;
        nint dstAddr = (nint)dst;

        // Y plane
        Parallel.For(0, height, y =>
        {
            byte* srcRow = (byte*)srcAddr + (long)y * srcStride;
            byte* yRow = (byte*)dstAddr + y * width;
            for (int x = 0; x < width; x++)
            {
                int b = srcRow[x * 4];
                int g = srcRow[x * 4 + 1];
                int r = srcRow[x * 4 + 2];
                yRow[x] = (byte)(((yr * r + yg * g + yb * b + 128) >> 8) + 16);
            }
        });

        // U and V planes (subsampled 2x2)
        Parallel.For(0, uvHeight, uvY =>
        {
            int srcY = uvY * 2;
            int srcY1 = Math.Min(srcY + 1, height - 1);
            byte* srcRow0 = (byte*)srcAddr + (long)srcY * srcStride;
            byte* srcRow1 = (byte*)srcAddr + (long)srcY1 * srcStride;
            byte* uRow = (byte*)dstAddr + uPlaneOffset + uvY * uvWidth;
            byte* vRow = (byte*)dstAddr + vPlaneOffset + uvY * uvWidth;

            for (int uvX = 0; uvX < uvWidth; uvX++)
            {
                int srcX = uvX * 2;
                int srcX1 = Math.Min(srcX + 1, width - 1);
                int off00 = srcX * 4;
                int off10 = srcX1 * 4;

                int b = srcRow0[off00] + srcRow0[off10] + srcRow1[off00] + srcRow1[off10];
                int g = srcRow0[off00 + 1] + srcRow0[off10 + 1] + srcRow1[off00 + 1] + srcRow1[off10 + 1];
                int r = srcRow0[off00 + 2] + srcRow0[off10 + 2] + srcRow1[off00 + 2] + srcRow1[off10 + 2];

                // Average of 4 pixels
                b = (b + 2) >> 2;
                g = (g + 2) >> 2;
                r = (r + 2) >> 2;

                uRow[uvX] = (byte)(((ur * r + ug * g + 112 * b + 128) >> 8) + 128);
                vRow[uvX] = (byte)(((vr * r + vg * g + vb * b + 128) >> 8) + 128);
            }
        });
    }

    public static unsafe void I420ToBgra(byte* src, byte* dst, int dstStride, int width, int height)
    {
        int yPlaneSize = width * height;
        int uvWidth = (width + 1) / 2;
        int uPlaneOffset = yPlaneSize;
        int vPlaneOffset = yPlaneSize + uvWidth * ((height + 1) / 2);

        nint srcAddr = (nint)src;
        nint dstAddr = (nint)dst;

        Parallel.For(0, height, y =>
        {
            byte* yRow = (byte*)srcAddr + y * width;
            byte* uRow = (byte*)srcAddr + uPlaneOffset + (y / 2) * uvWidth;
            byte* vRow = (byte*)srcAddr + vPlaneOffset + (y / 2) * uvWidth;
            byte* dstRow = (byte*)dstAddr + (long)y * dstStride;

            for (int x = 0; x < width; x++)
            {
                int c = yRow[x] - 16;
                int d = uRow[x / 2] - 128;
                int e = vRow[x / 2] - 128;

                dstRow[x * 4] = Clamp((298 * c + 516 * d + 128) >> 8);             // B
                dstRow[x * 4 + 1] = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8); // G
                dstRow[x * 4 + 2] = Clamp((298 * c + 409 * e + 128) >> 8);         // R
                dstRow[x * 4 + 3] = 255;                                            // A
            }
        });
    }

    public static unsafe void Yuy2ToBgra(byte* src, byte* dst, int dstStride, int width, int height)
        => Yuy2ToBgra(src, dst, dstStride, width, height, bt709: false, fullRange: false);

    public static unsafe void Yuy2ToBgra(
        byte* src, byte* dst, int dstStride, int width, int height, bool bt709, bool fullRange)
    {
        // YUV to full-range R'G'B' in 8.8 fixed point; the transfer function stays with the caller's color space.
        (int ym, int yo, int rv, int gu, int gv, int bu) = (bt709, fullRange) switch
        {
            (false, false) => (298, 16, 409, 100, 208, 516),
            (true, false) => (298, 16, 459, 55, 136, 541),
            (false, true) => (256, 0, 359, 88, 183, 454),
            (true, true) => (256, 0, 403, 48, 120, 475),
        };
        nint srcAddr = (nint)src;
        nint dstAddr = (nint)dst;

        Parallel.For(0, height, y =>
        {
            byte* srcRow = (byte*)srcAddr + y * width * 2;
            byte* dstRow = (byte*)dstAddr + (long)y * dstStride;
            int pairs = width / 2;

            for (int i = 0; i < pairs; i++)
            {
                int y0 = srcRow[i * 4];
                int u = srcRow[i * 4 + 1];
                int y1 = srcRow[i * 4 + 2];
                int v = srcRow[i * 4 + 3];

                int d = u - 128;
                int e = v - 128;

                // Pixel 0
                int c0 = ym * (y0 - yo);
                dstRow[i * 8] = Clamp((c0 + bu * d + 128) >> 8);             // B
                dstRow[i * 8 + 1] = Clamp((c0 - gu * d - gv * e + 128) >> 8); // G
                dstRow[i * 8 + 2] = Clamp((c0 + rv * e + 128) >> 8);         // R
                dstRow[i * 8 + 3] = 255;                                      // A

                // Pixel 1
                int c1 = ym * (y1 - yo);
                dstRow[i * 8 + 4] = Clamp((c1 + bu * d + 128) >> 8);             // B
                dstRow[i * 8 + 5] = Clamp((c1 - gu * d - gv * e + 128) >> 8); // G
                dstRow[i * 8 + 6] = Clamp((c1 + rv * e + 128) >> 8);         // R
                dstRow[i * 8 + 7] = 255;                                      // A
            }

            // Handle last pixel for odd width
            if (width % 2 != 0)
            {
                int off = pairs * 4;
                int y0 = srcRow[off];
                int u = srcRow[off + 1];

                int c0 = ym * (y0 - yo);
                int d = u - 128;
                int dstOff = pairs * 8;
                dstRow[dstOff] = Clamp((c0 + bu * d + 128) >> 8);     // B
                dstRow[dstOff + 1] = Clamp((c0 - gu * d + 128) >> 8); // G
                dstRow[dstOff + 2] = Clamp((c0 + 128) >> 8);          // R
                dstRow[dstOff + 3] = 255;                              // A
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }

    public static int GetI420BufferSize(int width, int height)
    {
        return width * height + ((width + 1) / 2) * ((height + 1) / 2) * 2;
    }
}
