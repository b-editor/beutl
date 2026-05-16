using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

public class AudioProcessContextTests
{
    [Test]
    public void GetSampleCount_Static_IntegerSeconds_NoCeiling()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(44100));
    }

    [Test]
    public void GetSampleCount_Static_ZeroDuration_ReturnsZero()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.Zero);
        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(0));
    }

    [Test]
    public void GetSampleCount_Static_FractionalDuration_CeilingsUp()
    {
        // 1 サンプル分のちょうど境界 (44100Hz, 1サンプル ≒ 226.7575 ticks) を 1tick だけ超えた duration。
        // 切り捨て版なら 1 サンプル、Ceiling 版なら 2 サンプル。回帰防止ケース。
        var oneSampleTicks = TimeSpan.TicksPerSecond / 44100; // 226
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(oneSampleTicks + 1));

        var truncated = (int)(range.Duration.TotalSeconds * 44100);
        var ceiled = AudioProcessContext.GetSampleCount(range, 44100);

        Assert.That(truncated, Is.EqualTo(1), "前提: 旧 truncation ロジックでは 1 サンプル");
        Assert.That(ceiled, Is.EqualTo(2), "修正後: Ceiling で 2 サンプルになるべき");
    }

    [Test]
    public void GetSampleCount_Static_MatchesInstanceMethod()
    {
        var range = new TimeRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromMilliseconds(123.456));
        const int sampleRate = 48000;

        var instance = new AudioProcessContext(range, sampleRate, new AnimationSampler(), null);

        Assert.That(AudioProcessContext.GetSampleCount(range, sampleRate),
            Is.EqualTo(instance.GetSampleCount()));
    }

    [Test]
    public void GetSampleCount_Static_DifferentSampleRates()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2));

        Assert.That(AudioProcessContext.GetSampleCount(range, 44100), Is.EqualTo(88200));
        Assert.That(AudioProcessContext.GetSampleCount(range, 48000), Is.EqualTo(96000));
    }
}
