using Beutl.Extensions.FFmpeg.Decoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegFrameValidationTests
{
    [TestCase(0, 1920, 1080)] // AV_PIX_FMT_YUV420P
    [TestCase(2, 2560, 1440)] // AV_PIX_FMT_RGB24
    [TestCase(0, 1, 1)]
    public void IsUsableVideoFrame_True_ForDecodedFrame(int pixelFormat, int width, int height)
    {
        Assert.That(FFmpegFrameValidation.IsUsableVideoFrame(pixelFormat, width, height), Is.True);
    }

    [TestCase(-1, 2560, 1440)] // AV_PIX_FMT_NONE: unreferenced after EAGAIN/EOF
    [TestCase(-1, 0, 0)]
    [TestCase(0, 0, 1080)]
    [TestCase(0, 1920, 0)]
    [TestCase(0, -1, 1080)]
    [TestCase(0, 1920, -1)]
    public void IsUsableVideoFrame_False_ForUnusableFrame(int pixelFormat, int width, int height)
    {
        Assert.That(FFmpegFrameValidation.IsUsableVideoFrame(pixelFormat, width, height), Is.False);
    }
}
