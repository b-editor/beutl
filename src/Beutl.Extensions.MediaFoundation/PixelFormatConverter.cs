#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation;
#else
namespace Beutl.Extensions.MediaFoundation;
#endif

// Software pixel-format conversions used by the Media Foundation encoder and as
// a CPU fallback when DXVA2's VideoProcessorMFT isn't available at decode time.
//
// These are intentionally straightforward single-threaded scalar loops:
// Media Foundation's Sink Writer / Source Reader already runs the heavy
// codecs on hardware when DXVA2/D3D11VA is on, so the YUV↔RGB conversion
// is not usually the bottleneck. Adding SIMD / Parallel.For here before
// profiling would be premature — keep it correct first.
//
// Matrix coefficients:
//   • BT.709 limited range for 8-bit NV12 (Y: 16..235, UV: 16..240)
//   • BT.2020 non-constant luminance limited range for 10-bit P010
//     (Y: 64..940, UV: 64..960 in a 10-bit scale; stored MSB-aligned in 16-bit)
internal static unsafe class PixelFormatConverter
{
    // ---------- SDR: BGRA8888 ↔ NV12 (BT.709 limited) ----------

    // BT.709 YCbCr, studio/limited range
    // Y  =  0.2126 R + 0.7152 G + 0.0722 B
    // Cb = -0.1146 R − 0.3854 G + 0.5    B
    // Cr =  0.5    R − 0.4542 G − 0.0458 B
    // Fixed-point: scaled by 1<<8 for the 8-bit path.
    public static void BgraToNv12(
        byte* src, int srcStride,
        byte* dst, int dstStride,
        int width, int height)
    {
        int yPlaneBytes = dstStride * height;
        byte* yPlane = dst;
        byte* uvPlane = dst + yPlaneBytes;

        // Y plane (full resolution)
        for (int y = 0; y < height; y++)
        {
            byte* srcRow = src + (long)y * srcStride;
            byte* yRow = yPlane + (long)y * dstStride;
            for (int x = 0; x < width; x++)
            {
                int b = srcRow[x * 4 + 0];
                int g = srcRow[x * 4 + 1];
                int r = srcRow[x * 4 + 2];
                // (54*R + 183*G + 18*B + 128) >> 8 → approximates 0.2126, 0.7152, 0.0722.
                // Then shift into 16..235 studio range.
                int yVal = ((54 * r + 183 * g + 18 * b + 128) >> 8) * 219 / 255 + 16;
                yRow[x] = ClampByte(yVal);
            }
        }

        // Interleaved UV plane (half resolution in both dimensions, 2x2 subsampling)
        int uvHeight = (height + 1) / 2;
        int uvWidth = (width + 1) / 2;
        for (int y = 0; y < uvHeight; y++)
        {
            int srcY0 = y * 2;
            int srcY1 = System.Math.Min(srcY0 + 1, height - 1);
            byte* row0 = src + (long)srcY0 * srcStride;
            byte* row1 = src + (long)srcY1 * srcStride;
            byte* uvRow = uvPlane + (long)y * dstStride;
            for (int x = 0; x < uvWidth; x++)
            {
                int sx0 = x * 2;
                int sx1 = System.Math.Min(sx0 + 1, width - 1);
                int b00 = row0[sx0 * 4 + 0], g00 = row0[sx0 * 4 + 1], r00 = row0[sx0 * 4 + 2];
                int b01 = row0[sx1 * 4 + 0], g01 = row0[sx1 * 4 + 1], r01 = row0[sx1 * 4 + 2];
                int b10 = row1[sx0 * 4 + 0], g10 = row1[sx0 * 4 + 1], r10 = row1[sx0 * 4 + 2];
                int b11 = row1[sx1 * 4 + 0], g11 = row1[sx1 * 4 + 1], r11 = row1[sx1 * 4 + 2];
                int r = (r00 + r01 + r10 + r11 + 2) >> 2;
                int g = (g00 + g01 + g10 + g11 + 2) >> 2;
                int b = (b00 + b01 + b10 + b11 + 2) >> 2;

                // Cb: (-29*R − 99*G + 128*B + 128) >> 8 → −0.1146, −0.3854, 0.5
                // Cr: (128*R − 116*G − 12*B + 128) >> 8 →  0.5,    −0.4542, −0.0458
                int cb = (-29 * r - 99 * g + 128 * b + 128) >> 8;
                int cr = (128 * r - 116 * g - 12 * b + 128) >> 8;
                // Into 16..240 studio range, centered on 128.
                cb = cb * 224 / 255 + 128;
                cr = cr * 224 / 255 + 128;
                uvRow[x * 2 + 0] = ClampByte(cb);
                uvRow[x * 2 + 1] = ClampByte(cr);
            }
        }
    }

    public static void Nv12ToBgra(
        byte* src, int srcStride,
        byte* dst, int dstStride,
        int width, int height)
    {
        byte* yPlane = src;
        byte* uvPlane = src + (long)srcStride * height;

        for (int y = 0; y < height; y++)
        {
            byte* yRow = yPlane + (long)y * srcStride;
            byte* uvRow = uvPlane + (long)(y >> 1) * srcStride;
            byte* dstRow = dst + (long)y * dstStride;
            for (int x = 0; x < width; x++)
            {
                int yv = yRow[x] - 16;
                int cb = uvRow[(x & ~1) + 0] - 128;
                int cr = uvRow[(x & ~1) + 1] - 128;
                // Inverse BT.709 limited range.
                int r = (298 * yv + 459 * cr + 128) >> 8;
                int g = (298 * yv - 55 * cb - 136 * cr + 128) >> 8;
                int b = (298 * yv + 541 * cb + 128) >> 8;
                dstRow[x * 4 + 0] = ClampByte(b);
                dstRow[x * 4 + 1] = ClampByte(g);
                dstRow[x * 4 + 2] = ClampByte(r);
                dstRow[x * 4 + 3] = 255;
            }
        }
    }

    // ---------- HDR: RGBA16161616 ↔ P010 (BT.2020 NCL limited, 10-bit) ----------

    // RGBA16 is 16-bit per channel (full precision), P010 packs 10-bit samples in
    // the high 10 bits of a 16-bit container. The encoder's perspective is studio
    // range (Y 64..940, UV 64..960 on a 10-bit scale) → left-shift by 6 to occupy
    // the MSBs exactly as P010 demands.
    public static void Rgba16ToP010(
        byte* src, int srcStride,
        byte* dst, int dstStride,
        int width, int height)
    {
        ushort* y16Plane = (ushort*)dst;
        ushort* uv16Plane = (ushort*)(dst + (long)dstStride * height);
        int strideShorts = dstStride / 2;

        for (int y = 0; y < height; y++)
        {
            ushort* srcRow = (ushort*)(src + (long)y * srcStride);
            ushort* yRow = y16Plane + (long)y * strideShorts;
            for (int x = 0; x < width; x++)
            {
                int r = srcRow[x * 4 + 0];
                int g = srcRow[x * 4 + 1];
                int b = srcRow[x * 4 + 2];
                // BT.2020 luma: 0.2627 R + 0.6780 G + 0.0593 B (in 16-bit full-scale)
                // → 10-bit limited range: map 0..65535 → 64..940 then << 6
                long luma16 = (17235L * r + 44461L * g + 3891L * b + 32768L) >> 16;
                int y10 = (int)(luma16 * 876 / 65535 + 64);
                yRow[x] = (ushort)(Clamp10(y10) << 6);
            }
        }

        int uvHeight = (height + 1) / 2;
        int uvWidth = (width + 1) / 2;
        for (int y = 0; y < uvHeight; y++)
        {
            int sy0 = y * 2;
            int sy1 = System.Math.Min(sy0 + 1, height - 1);
            ushort* row0 = (ushort*)(src + (long)sy0 * srcStride);
            ushort* row1 = (ushort*)(src + (long)sy1 * srcStride);
            ushort* uvRow = uv16Plane + (long)y * strideShorts;
            for (int x = 0; x < uvWidth; x++)
            {
                int sx0 = x * 2;
                int sx1 = System.Math.Min(sx0 + 1, width - 1);
                int r = (row0[sx0 * 4 + 0] + row0[sx1 * 4 + 0] + row1[sx0 * 4 + 0] + row1[sx1 * 4 + 0] + 2) >> 2;
                int g = (row0[sx0 * 4 + 1] + row0[sx1 * 4 + 1] + row1[sx0 * 4 + 1] + row1[sx1 * 4 + 1] + 2) >> 2;
                int b = (row0[sx0 * 4 + 2] + row0[sx1 * 4 + 2] + row1[sx0 * 4 + 2] + row1[sx1 * 4 + 2] + 2) >> 2;
                long luma16 = (17235L * r + 44461L * g + 3891L * b + 32768L) >> 16;
                // Cb = (B − Y) / 1.8814 , Cr = (R − Y) / 1.4746
                long cbNum = ((long)b << 16) - (luma16 << 16);
                long crNum = ((long)r << 16) - (luma16 << 16);
                long cb16 = cbNum / 123269; // 1.8814 * 65536
                long cr16 = crNum / 96639;  // 1.4746 * 65536
                // Shift signed range into 10-bit limited (512 center, 64..960)
                int cb10 = (int)(cb16 * 896 / (65535 * 2) + 512);
                int cr10 = (int)(cr16 * 896 / (65535 * 2) + 512);
                uvRow[x * 2 + 0] = (ushort)(Clamp10(cb10) << 6);
                uvRow[x * 2 + 1] = (ushort)(Clamp10(cr10) << 6);
            }
        }
    }

    public static void P010ToRgba16(
        byte* src, int srcStride,
        byte* dst, int dstStride,
        int width, int height)
    {
        ushort* y16Plane = (ushort*)src;
        ushort* uv16Plane = (ushort*)(src + (long)srcStride * height);
        int strideShorts = srcStride / 2;

        for (int y = 0; y < height; y++)
        {
            ushort* yRow = y16Plane + (long)y * strideShorts;
            ushort* uvRow = uv16Plane + (long)(y >> 1) * strideShorts;
            ushort* dstRow = (ushort*)(dst + (long)y * dstStride);
            for (int x = 0; x < width; x++)
            {
                // Strip P010's MSB packing → 10-bit value in [0, 1023]
                int yv = (yRow[x] >> 6) - 64;
                int cb = ((uvRow[(x & ~1) + 0]) >> 6) - 512;
                int cr = ((uvRow[(x & ~1) + 1]) >> 6) - 512;
                // Inverse BT.2020 NCL limited: scale 10-bit diff back into full-range R/G/B.
                long rY = (long)yv * 65535 / 876;
                long rCr = (long)cr * 96639L / 448L;   // 1.4746 / 0.5
                long rCb = (long)cb * 123269L / 448L;  // 1.8814 / 0.5
                long r = rY + rCr;
                // G = Y − (0.2627/0.6780) Cr − (0.0593/0.6780) Cb
                long g = rY - rCr * 17235L / 44461L - rCb * 3891L / 44461L;
                long b = rY + rCb;
                dstRow[x * 4 + 0] = Clamp16((int)r);
                dstRow[x * 4 + 1] = Clamp16((int)g);
                dstRow[x * 4 + 2] = Clamp16((int)b);
                dstRow[x * 4 + 3] = 65535;
            }
        }
    }

    private static byte ClampByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
    private static int Clamp10(int v) => v < 0 ? 0 : v > 1023 ? 1023 : v;
    private static ushort Clamp16(int v) => (ushort)(v < 0 ? 0 : v > 65535 ? 65535 : v);
}
