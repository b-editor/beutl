using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SourceSoundThumbnailTests
{
    [TestCase(1, 44100)]
    [TestCase(44, 44100)]
    [TestCase(45, 44100)]
    [TestCase(1024, 48000)]
    public void GetWaveformChunkDuration_MapsBackToRequestedSampleCount(int sampleCount, int sampleRate)
    {
        TimeSpan duration = SourceSound.GetWaveformChunkDuration(sampleCount, sampleRate);

        Assert.That(
            AudioProcessContext.GetSampleCount(new TimeRange(TimeSpan.Zero, duration), sampleRate),
            Is.EqualTo(sampleCount));
    }

    [Test]
    public void GetWaveformChunkDuration_AdjacentBoundariesDifferByAtMostOneTick()
    {
        const int sampleRate = 44100;
        const int totalSamples = 44100;
        const int chunkCount = 1000;

        for (int chunkIndex = 0; chunkIndex < chunkCount - 1; chunkIndex++)
        {
            int startSample = (int)((long)chunkIndex * totalSamples / chunkCount);
            int endSample = (int)((long)(chunkIndex + 1) * totalSamples / chunkCount);
            var start = TimeSpan.FromSeconds((double)startSample / sampleRate);
            var duration = SourceSound.GetWaveformChunkDuration(endSample - startSample, sampleRate);
            var nextStart = TimeSpan.FromSeconds((double)endSample / sampleRate);

            Assert.That(Math.Abs((start + duration - nextStart).Ticks), Is.LessThanOrEqualTo(1));
        }
    }
}
