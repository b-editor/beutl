using Beutl.Extensions.FFmpeg.Decoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegVideoSlotSizingTests
{
    [TestCase(false, 4)] // BGRA
    [TestCase(true, 8)] // RGBA64LE
    public void GetBytesPerPixel_MatchesDeliveredPixelFormat(bool isHdr, int expected)
    {
        Assert.That(FFmpegVideoSlotSizing.GetBytesPerPixel(isHdr), Is.EqualTo(expected));
    }

    // Regression: SDR slots were sized for 8 bytes per pixel, so every open reader committed twice the
    // shared memory it could use (about 265 MB instead of 133 MB for a 4K reader's four slots).
    [Test]
    public void GetSlotSize_Sdr_UsesFourBytesPerPixel()
    {
        Assert.That(FFmpegVideoSlotSizing.GetSlotSize(3840, 2160, isHdr: false), Is.EqualTo(3840L * 2160 * 4 + 64));
    }

    [Test]
    public void GetSlotSize_Hdr_UsesEightBytesPerPixel()
    {
        Assert.That(FFmpegVideoSlotSizing.GetSlotSize(3840, 2160, isHdr: true), Is.EqualTo(3840L * 2160 * 8 + 64));
    }

    [Test]
    public void GetSlotSize_DoesNotOverflowForLargeFrames()
    {
        Assert.That(FFmpegVideoSlotSizing.GetSlotSize(16384, 16384, isHdr: true), Is.EqualTo(16384L * 16384 * 8 + 64));
    }
}
