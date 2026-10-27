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
}
