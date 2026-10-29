using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media;

public class YuvConversionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public unsafe void BgraToI420_UsesTheSelectedMatrixWithPaddedRowsAndOddDimensions(bool bt709)
    {
        const int width = 11, height = 3, stride = width * 4 + 16;
        byte[][] colors = [[0, 0, 255], [0, 255, 0], [255, 0, 0], [128, 128, 128], [0, 0, 0], [255, 255, 255]];
        byte[] source = new byte[stride * height];
        Array.Fill(source, (byte)0xCD);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                colors[x / 2].CopyTo(source, y * stride + x * 4);
                source[y * stride + x * 4 + 3] = 255;
            }
        byte[] result = new byte[YuvConversion.GetI420BufferSize(width, height)];
        fixed (byte* src = source, dst = result)
        {
            if (bt709)
                YuvConversion.BgraToI420(src, stride, dst, width, height, bt709: true);
            else
                YuvConversion.BgraToI420(src, stride, dst, width, height);
        }

        int[][] expected = bt709
            ? [[63, 102, 240], [173, 42, 26], [32, 240, 118], [126, 128, 128], [16, 128, 128], [235, 128, 128]]
            : [[81, 90, 240], [145, 54, 34], [41, 240, 110], [126, 128, 128], [16, 128, 128], [235, 128, 128]];
        int uvWidth = (width + 1) / 2, uvHeight = (height + 1) / 2;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Assert.That(result[y * width + x], Is.EqualTo(expected[x / 2][0]).Within(1));
        for (int y = 0; y < uvHeight; y++)
            for (int x = 0; x < uvWidth; x++)
            {
                Assert.That(result[width * height + y * uvWidth + x], Is.EqualTo(expected[x][1]).Within(1));
                Assert.That(result[width * height + uvWidth * uvHeight + y * uvWidth + x], Is.EqualTo(expected[x][2]).Within(1));
            }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public unsafe void Yuy2ToBgra_UsesTheSelectedMatrixAndRange(bool bt709, bool fullRange)
    {
        // Red, green, blue, gray 128, black and white, encoded by independent reference conversions.
        int[][] yuv = (bt709, fullRange) switch
        {
            (false, false) => [[81, 90, 240], [145, 54, 34], [41, 240, 110], [126, 128, 128], [16, 128, 128], [235, 128, 128]],
            (true, false) => [[63, 102, 240], [173, 42, 26], [32, 240, 118], [126, 128, 128], [16, 128, 128], [235, 128, 128]],
            (false, true) => [[76, 85, 255], [150, 44, 21], [29, 255, 107], [128, 128, 128], [0, 128, 128], [255, 128, 128]],
            (true, true) => [[54, 99, 255], [182, 30, 12], [18, 255, 116], [128, 128, 128], [0, 128, 128], [255, 128, 128]],
        };
        byte[][] rgb = [[255, 0, 0], [0, 255, 0], [0, 0, 255], [128, 128, 128], [0, 0, 0], [255, 255, 255]];
        const int width = 12, height = 2, stride = width * 4 + 8;
        byte[] source = new byte[width * 2 * height];
        for (int y = 0; y < height; y++)
            for (int pair = 0; pair < width / 2; pair++)
            {
                int offset = y * width * 2 + pair * 4;
                (source[offset], source[offset + 1], source[offset + 2], source[offset + 3]) =
                    ((byte)yuv[pair][0], (byte)yuv[pair][1], (byte)yuv[pair][0], (byte)yuv[pair][2]);
            }
        byte[] result = new byte[stride * height];
        fixed (byte* src = source, dst = result)
            YuvConversion.Yuy2ToBgra(src, dst, stride, width, height, bt709, fullRange);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                byte[] expected = rgb[x / 2];
                Assert.That(result[offset + 2], Is.EqualTo(expected[0]).Within(3), $"R at {x},{y}");
                Assert.That(result[offset + 1], Is.EqualTo(expected[1]).Within(3), $"G at {x},{y}");
                Assert.That(result[offset], Is.EqualTo(expected[2]).Within(3), $"B at {x},{y}");
                Assert.That(result[offset + 3], Is.EqualTo(255));
            }
    }
}
