using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

public class AudioFrameSnapshotTests
{
    [Test]
    public void SampleCount_DerivedFromInterleavedLength()
    {
        // 4 frames * 2 channels = 8 samples interleaved
        var snapshot = new AudioFrameSnapshot(new float[8], 48000, 2, TimeSpan.Zero);
        Assert.That(snapshot.SampleCount, Is.EqualTo(4));
    }

    [Test]
    public void SampleCount_ZeroChannels_ReturnsZero()
    {
        var snapshot = new AudioFrameSnapshot([], 48000, 0, TimeSpan.Zero);
        Assert.That(snapshot.SampleCount, Is.EqualTo(0));
    }

    [Test]
    public void Duration_FromSampleCountAndRate()
    {
        // 48000 samples per channel @ 48000 Hz = 1s
        var snapshot = new AudioFrameSnapshot(new float[48000 * 2], 48000, 2, TimeSpan.Zero);
        Assert.That(snapshot.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Duration_ZeroSampleRate_ReturnsZero()
    {
        var snapshot = new AudioFrameSnapshot(new float[8], 0, 2, TimeSpan.FromSeconds(5));
        Assert.That(snapshot.Duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void StartTime_PreservedFromConstruction()
    {
        var t = TimeSpan.FromSeconds(2.5);
        var snapshot = new AudioFrameSnapshot(new float[4], 44100, 2, t);
        Assert.That(snapshot.StartTime, Is.EqualTo(t));
    }
}
