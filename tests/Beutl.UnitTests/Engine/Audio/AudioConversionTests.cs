using Beutl.Audio;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioConversionTests
{
    [Test]
    public void Interleave_WritesLeftThenRightForEachSample()
    {
        using AudioBuffer audio = CreateStereo([0.1f, 0.2f], [-0.1f, -0.2f]);
        var destination = new float[4];

        AudioConversion.Interleave(audio, destination);

        Assert.That(destination, Is.EqualTo(new[] { 0.1f, -0.1f, 0.2f, -0.2f }));
    }

    // Values past full scale saturate instead of wrapping.
    [Test]
    public void Interleave_ToInt16_RoundsAwayFromZeroAndSaturates()
    {
        using AudioBuffer audio = CreateStereo([0f, 0.5f, 1f, 2f], [-0f, -0.5f, -1f, -2f]);
        var destination = new short[8];

        AudioConversion.Interleave(audio, destination);

        Assert.That(destination, Is.EqualTo(new short[] { 0, 0, 16384, -16384, 32767, -32767, 32767, -32768 }));
    }

    [Test]
    public void Deinterleave_SplitsLeftAndRightSamples()
    {
        using var audio = new AudioBuffer(48000, 2, 2);

        AudioConversion.Deinterleave([0.1f, -0.1f, 0.2f, -0.2f], audio);

        Assert.Multiple(() =>
        {
            Assert.That(audio.GetChannelData(0).ToArray(), Is.EqualTo(new[] { 0.1f, 0.2f }));
            Assert.That(audio.GetChannelData(1).ToArray(), Is.EqualTo(new[] { -0.1f, -0.2f }));
            Assert.That(() => AudioConversion.Deinterleave(new float[3], audio), Throws.ArgumentException);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    public void Conversion_RoundTripsEveryChannelAndKeepsTheExtraDestinationTail(int channels)
    {
        using var source = AudioTestBuffers.CreateBuffer(channels, 4, (ch, i) => (ch * 4 + i) / 32f);
        using var result = new AudioBuffer(source.SampleRate, channels, 4);
        var interleaved = Enumerable.Repeat(99f, 4 * channels + 1).ToArray();
        var pcm = new short[4 * channels];
        AudioConversion.Interleave(source, interleaved);
        AudioConversion.Interleave(source, pcm);
        AudioConversion.Deinterleave(interleaved, result);
        for (int ch = 0; ch < channels; ch++)
        {
            Assert.That(result.GetChannelData(ch).ToArray(), Is.EqualTo(source.GetChannelData(ch).ToArray()));
            for (int i = 0; i < 4; i++)
            {
                float sample = source.GetChannelData(ch)[i];
                Assert.That(interleaved[i * channels + ch], Is.EqualTo(sample));
                Assert.That(pcm[i * channels + ch], Is.EqualTo((short)MathF.Round(sample * short.MaxValue, MidpointRounding.AwayFromZero)));
            }
        }
        Assert.That(interleaved[^1], Is.EqualTo(99f));
        Assert.Throws<ArgumentException>(() => AudioConversion.Interleave(source, new float[4 * channels - 1]));
        Assert.Throws<ArgumentException>(() => AudioConversion.Interleave(source, new short[4 * channels - 1]));
        Assert.Throws<ArgumentException>(() => AudioConversion.Deinterleave(new float[4 * channels - 1], result));
    }

    private static AudioBuffer CreateStereo(float[] left, float[] right)
    {
        var audio = new AudioBuffer(48000, 2, left.Length);
        left.CopyTo(audio.GetChannelData(0));
        right.CopyTo(audio.GetChannelData(1));
        return audio;
    }
}
