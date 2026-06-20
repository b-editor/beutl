using Beutl.Embedding.MediaFoundation.Decoding;

using Windows.Win32.Media.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class TimestampUtilitiesTests
{
    private static MFRatio Ratio(int numerator, int denominator)
        => new() { Numerator = (uint)numerator, Denominator = (uint)denominator };

    [TestCase(0L, 0.0)]
    [TestCase(10_000_000L, 1.0)]
    [TestCase(5_000_000L, 0.5)]
    [TestCase(20_000_000L, 2.0)]
    public void ConvertSecFrom100ns_KnownValues(long hns, double expectedSeconds)
        => Assert.That(TimestampUtilities.ConvertSecFrom100ns(hns), Is.EqualTo(expectedSeconds).Within(1e-9));

    [TestCase(0.0, 0L)]
    [TestCase(1.0, 10_000_000L)]
    [TestCase(0.5, 5_000_000L)]
    public void Convert100nsFromSec_KnownValues(double seconds, long expectedHns)
        => Assert.That(TimestampUtilities.Convert100nsFromSec(seconds), Is.EqualTo(expectedHns));

    [Test]
    public void ConvertFrameFromTimeStamp_OneSecondAt30fps_Returns30()
        => Assert.That(TimestampUtilities.ConvertFrameFromTimeStamp(10_000_000, Ratio(30, 1)), Is.EqualTo(30));

    [Test]
    public void ConvertFrameFromTimeStamp_RoundsHalfAwayFromZero()
        // 0.5s at 1 fps == exactly 0.5 frames; AwayFromZero rounds to 1 (banker's rounding would give 0).
        => Assert.That(TimestampUtilities.ConvertFrameFromTimeStamp(5_000_000, Ratio(1, 1)), Is.EqualTo(1));

    [Test]
    public void ConvertTimeStampFromFrame_30FramesAt30fps_ReturnsOneSecond()
        => Assert.That(TimestampUtilities.ConvertTimeStampFromFrame(30, Ratio(30, 1)), Is.EqualTo(10_000_000));

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(29)]
    [TestCase(30)]
    [TestCase(150)]
    public void FrameAndTimeStamp_RoundTripAt30fps(int frame)
    {
        MFRatio rate = Ratio(30, 1);
        long timestamp = TimestampUtilities.ConvertTimeStampFromFrame(frame, rate);
        Assert.That(TimestampUtilities.ConvertFrameFromTimeStamp(timestamp, rate), Is.EqualTo(frame));
    }

    [Test]
    public void ConvertSampleFromTimeStamp_OneSecondAt44100_Returns44100()
        => Assert.That(TimestampUtilities.ConvertSampleFromTimeStamp(10_000_000, 44100), Is.EqualTo(44100));

    [Test]
    public void ConvertTimeStampFromSample_44100SamplesAt44100_ReturnsOneSecond()
        => Assert.That(TimestampUtilities.ConvertTimeStampFromSample(44100, 44100), Is.EqualTo(10_000_000));

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(22050)]
    [TestCase(44100)]
    public void SampleAndTimeStamp_RoundTripAt44100(int sample)
    {
        long timestamp = TimestampUtilities.ConvertTimeStampFromSample(sample, 44100);
        Assert.That(TimestampUtilities.ConvertSampleFromTimeStamp(timestamp, 44100), Is.EqualTo(sample));
    }
}
