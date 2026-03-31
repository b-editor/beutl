using System.Runtime.CompilerServices;

namespace Beutl.Media;

public static class YuvConversion
{
    // BT.601 coefficients (same as OpenCV)

    public static unsafe void BgraToI420(byte* src, byte* dst, int width, int height)
    {
        int yPlaneSize = width * height;
        int uvWidth = width / 2;
        int uvHeight = height / 2;
        int uPlaneOffset = yPlaneSize;
        int vPlaneOffset = yPlaneSize + uvWidth * uvHeight;

        nint srcAddr = (nint)src;
        nint dstAddr = (nint)dst;

        // Y plane
        Parallel.For(0, height, y =>
        {
            byte* srcRow = (byte*)srcAddr + y * width * 4;
            byte* yRow = (byte*)dstAddr + y * width;
            for (int x = 0; x < width; x++)
            {
                int b = srcRow[x * 4];
                int g = srcRow[x * 4 + 1];
                int r = srcRow[x * 4 + 2];
                yRow[x] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
            }
        });

        // U and V planes (subsampled 2x2)
        Parallel.For(0, uvHeight, uvY =>
        {
            int srcY = uvY * 2;
            byte* srcRow0 = (byte*)srcAddr + srcY * width * 4;
            byte* srcRow1 = (byte*)srcAddr + (srcY + 1) * width * 4;
            byte* uRow = (byte*)dstAddr + uPlaneOffset + uvY * uvWidth;
            byte* vRow = (byte*)dstAddr + vPlaneOffset + uvY * uvWidth;

            for (int uvX = 0; uvX < uvWidth; uvX++)
            {
                int srcX = uvX * 2;
                int off00 = srcX * 4;
                int off10 = (srcX + 1) * 4;

                int b = srcRow0[off00] + srcRow0[off10] + srcRow1[off00] + srcRow1[off10];
                int g = srcRow0[off00 + 1] + srcRow0[off10 + 1] + srcRow1[off00 + 1] + srcRow1[off10 + 1];
                int r = srcRow0[off00 + 2] + srcRow0[off10 + 2] + srcRow1[off00 + 2] + srcRow1[off10 + 2];

                // Average of 4 pixels
                b = (b + 2) >> 2;
                g = (g + 2) >> 2;
                r = (r + 2) >> 2;

                uRow[uvX] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                vRow[uvX] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        });
    }

    public static unsafe void I420ToBgra(byte* src, byte* dst, int width, int height)
    {
        int yPlaneSize = width * height;
        int uvWidth = width / 2;
        int uPlaneOffset = yPlaneSize;
        int vPlaneOffset = yPlaneSize + uvWidth * (height / 2);

        nint srcAddr = (nint)src;
        nint dstAddr = (nint)dst;

        Parallel.For(0, height, y =>
        {
            byte* yRow = (byte*)srcAddr + y * width;
            byte* uRow = (byte*)srcAddr + uPlaneOffset + (y / 2) * uvWidth;
            byte* vRow = (byte*)srcAddr + vPlaneOffset + (y / 2) * uvWidth;
            byte* dstRow = (byte*)dstAddr + y * width * 4;

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

    public static unsafe void Yuy2ToBgra(byte* src, byte* dst, int width, int height)
    {
        nint srcAddr = (nint)src;
        nint dstAddr = (nint)dst;

        Parallel.For(0, height, y =>
        {
            byte* srcRow = (byte*)srcAddr + y * width * 2;
            byte* dstRow = (byte*)dstAddr + y * width * 4;
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
                int c0 = y0 - 16;
                dstRow[i * 8] = Clamp((298 * c0 + 516 * d + 128) >> 8);             // B
                dstRow[i * 8 + 1] = Clamp((298 * c0 - 100 * d - 208 * e + 128) >> 8); // G
                dstRow[i * 8 + 2] = Clamp((298 * c0 + 409 * e + 128) >> 8);         // R
                dstRow[i * 8 + 3] = 255;                                             // A

                // Pixel 1
                int c1 = y1 - 16;
                dstRow[i * 8 + 4] = Clamp((298 * c1 + 516 * d + 128) >> 8);             // B
                dstRow[i * 8 + 5] = Clamp((298 * c1 - 100 * d - 208 * e + 128) >> 8); // G
                dstRow[i * 8 + 6] = Clamp((298 * c1 + 409 * e + 128) >> 8);         // R
                dstRow[i * 8 + 7] = 255;                                             // A
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
        return width * height + (width / 2) * (height / 2) * 2;
    }
}
