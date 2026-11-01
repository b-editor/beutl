using Beutl.Audio;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class AudioSampleRingBufferTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(8)]
    public void SnapshotChannelsAreFoldedWithTheSharedMatrix(int channels)
    {
        using var input = new AudioBuffer(48000, channels, 32);
        for (int ch = 0; ch < channels; ch++) input.GetChannelData(ch).Fill((ch + 1) / 32f);
        var samples = new float[32 * channels];
        AudioConversion.Interleave(input, samples);
        using var expected = AudioChannelMatrix.Create(AudioChannelLayout.GetDefault(channels), AudioChannelLayout.Stereo).Apply(input);
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(samples, channels, 48000, MediaTime.Zero);
        var left = new float[32];
        var right = new float[32];
        ring.ReadAroundTime(MediaTime.FromSamples(32, 48000), left, right, 32);
        Assert.That(left, Is.EqualTo(expected.GetChannelData(0).ToArray()));
        Assert.That(right, Is.EqualTo(expected.GetChannelData(1).ToArray()));
    }
}
