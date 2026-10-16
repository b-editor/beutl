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

    [Test]
    public void Interleave_RejectsAMonoBufferAndAShortDestination()
    {
        using var mono = new AudioBuffer(48000, 1, 2);
        using AudioBuffer stereo = CreateStereo([0f, 0f], [0f, 0f]);

        Assert.Multiple(() =>
        {
            Assert.That(() => AudioConversion.Interleave(mono, new float[4]), Throws.ArgumentException);
            Assert.That(() => AudioConversion.Interleave(stereo, new float[3]), Throws.ArgumentException);
        });
    }

    private static AudioBuffer CreateStereo(float[] left, float[] right)
    {
        var audio = new AudioBuffer(48000, 2, left.Length);
        left.CopyTo(audio.GetChannelData(0));
        right.CopyTo(audio.GetChannelData(1));
        return audio;
    }
}
