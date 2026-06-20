using System;
using Beutl.Audio.Graph;
using NUnit.Framework;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioMathTests
{
    [TestCase(0.0, 44100, ExpectedResult = 0L)]
    [TestCase(1.0, 44100, ExpectedResult = 44100L)]
    [TestCase(0.5, 48000, ExpectedResult = 24000L)]
    [TestCase(2.0, 192000, ExpectedResult = 384000L)]
    public long TimeToSampleIndex_NormalTimes(double seconds, int sampleRate)
    {
        return AudioMath.TimeToSampleIndex(TimeSpan.FromSeconds(seconds), sampleRate);
    }

    // Regression: past int.MaxValue samples an (int) cast would wrap negative; the helper must
    // return the exact long value.
    [Test]
    public void TimeToSampleIndex_LongTimelineReturnsExactLongNotWrappedNegative()
    {
        // 7 h @ 192 kHz = 4,838,400,000 samples, well past int.MaxValue (2,147,483,647).
        long result = AudioMath.TimeToSampleIndex(TimeSpan.FromHours(7), 192000);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(7L * 3600 * 192000));
            Assert.That(result, Is.GreaterThan((long)int.MaxValue));
        });
    }

    [Test]
    public void TimeToSampleIndex_NegativeTimeReturnsNegativeLong()
    {
        // The helper reports the true (negative) position for a time before zero; callers clamp if needed.
        Assert.That(
            AudioMath.TimeToSampleIndex(TimeSpan.FromSeconds(-1.5), 48000),
            Is.EqualTo(-72000L));
    }

    [Test]
    public void TimeToSampleIndex_NonPositiveSampleRateThrows()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => AudioMath.TimeToSampleIndex(TimeSpan.Zero, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => AudioMath.TimeToSampleIndex(TimeSpan.Zero, -1));
        });
    }
}
