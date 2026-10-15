using Beutl.Editor.Models;

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
}
