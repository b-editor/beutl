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

    // Regression for the audio-graph Int32 overflow: past int.MaxValue samples an unchecked (int)
    // cast would silently wrap to int.MinValue and feed a negative offset / wrong seek. The helper
    // must return the exact long value instead.
    [Test]
    public void TimeToSampleIndex_LongTimelineReturnsExactLongNotWrappedNegative()
    {
        // 7 h @ 192 kHz = 4,838,400,000 samples (> int.MaxValue = 2,147,483,647). Reaches the
        // overflow regime at ~3.1 h at this rate.
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
        // Callers that need a non-negative index clamp/guard themselves; the helper reports the
        // true (negative) sample position for a time before zero.
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
