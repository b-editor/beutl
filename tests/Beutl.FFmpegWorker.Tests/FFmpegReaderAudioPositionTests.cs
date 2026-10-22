using Beutl.FFmpegWorker.Decoding;

namespace Beutl.FFmpegWorker.Tests;

// The audio read copies only from inside a frame, whatever PTS a file gives its frames.
public sealed class FFmpegReaderAudioPositionTests
{
    [TestCase(1500L, 1024L, 1024, 476)]
    [TestCase(1024L, 1024L, 1024, 0)]
    [TestCase(2047L, 1024L, 1024, 1023)]
    [TestCase(2048L, 1024L, 1024, -1)]
    [TestCase(1000L, 1024L, 1024, -1)]
    // A frame 3e9 samples before the read, which a 32-bit offset turned negative.
    [TestCase(1_000_000_000L, -2_000_000_000L, 1024, -1)]
    public void OffsetInFrame_is_inside_the_frame_or_minus_one(long position, long frameStart, int frameSamples, int expected)
    {
        Assert.That(FFmpegReader.OffsetInFrame(position, frameStart, frameSamples), Is.EqualTo(expected));
    }

    [TestCase(1.0, 48000L)]
    [TestCase(-0.5, -24000L)]
    [TestCase(45000.0, null)]
    [TestCase(-45000.0, null)]
    [TestCase(double.NaN, null)]
    public void SamplePosition_rejects_times_an_int_position_cannot_address(double seconds, long? expected)
    {
        Assert.That(FFmpegReader.SamplePosition(seconds, 48000), Is.EqualTo(expected));
    }
}
