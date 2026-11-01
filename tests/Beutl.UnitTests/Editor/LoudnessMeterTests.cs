using Beutl.Audio;
using Beutl.Editor.Components.AudioVisualizerTab.Utilities;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class LoudnessMeterTests
{
    private const int SampleRate = 48000;
    private const double FullScaleSineLkfs = -3.01;

    // BS.1770-5 Annex 1 Table 3 (5.1) and Annex 3 Table 5, configuration I (0+7+0):
    // M±110 and M±090 weigh 1.41, M±135 weighs 1.0, and LFE is left out.
    [Test]
    public void ChannelWeightsFollowBs1770()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Weights(AudioChannelLayout.Stereo), Is.EqualTo(new[] { 1.0, 1.0 }));
            Assert.That(Weights(AudioChannelLayout.Surround51), Is.EqualTo(new[] { 1.0, 1.0, 1.0, 0, 1.41, 1.41 }));
            Assert.That(Weights(AudioChannelLayout.Surround71), Is.EqualTo(new[] { 1.0, 1.0, 1.0, 0, 1.0, 1.0, 1.41, 1.41 }));
        });

        static double[] Weights(AudioChannelLayout layout)
            => Enumerable.Range(0, layout.ChannelCount).Select(ch => LoudnessMeter.GetChannelWeight(layout, ch)).ToArray();
    }

    // A 0 dBFS 997 Hz sine in L, C or R reads -3.01 LKFS (BS.1770-5 Annex 1); a 1.41 weight adds ~1.49 dB.
    [TestCase(6, 2, FullScaleSineLkfs)]
    [TestCase(6, 4, FullScaleSineLkfs + 1.4922)]
    [TestCase(8, 4, FullScaleSineLkfs)]
    [TestCase(8, 6, FullScaleSineLkfs + 1.4922)]
    public void SineInOneChannelReadsItsWeightedLoudness(int channels, int channel, double expected)
    {
        float[][] samples = Signal(channels, channel);

        float lufs = new LoudnessMeter().Compute(samples, 0, samples[0].Length, AudioChannelLayout.GetDefault(channels), SampleRate);

        Assert.That(lufs, Is.EqualTo(expected).Within(0.05));
    }

    [TestCase(6)]
    [TestCase(8)]
    public void LowFrequencyChannelIsExcluded(int channels)
    {
        float[][] samples = Signal(channels, 3);

        float lufs = new LoudnessMeter().Compute(samples, 0, samples[0].Length, AudioChannelLayout.GetDefault(channels), SampleRate);

        Assert.That(lufs, Is.EqualTo(-160f));
    }

    [Test]
    public void StereoMatchesTheMultichannelPath()
    {
        float[][] samples = Signal(2, 0);
        samples[1] = samples[0].Select(v => v * 0.25f).ToArray();
        var meter = new LoudnessMeter();

        float stereo = meter.Compute(samples[0], samples[1], SampleRate);
        float multichannel = meter.Compute(samples, 0, samples[0].Length, AudioChannelLayout.Stereo, SampleRate);

        Assert.That(stereo, Is.EqualTo(multichannel).Within(1e-4f));
        Assert.That(stereo, Is.EqualTo(FullScaleSineLkfs + 10 * Math.Log10(1 + 0.25 * 0.25)).Within(0.05));
    }

    // 400 ms of a 0 dBFS 997 Hz sine in `channel`, silence elsewhere.
    private static float[][] Signal(int channels, int channel)
    {
        int n = SampleRate * 4 / 10;
        float[][] samples = Enumerable.Range(0, channels).Select(_ => new float[n]).ToArray();
        for (int i = 0; i < n; i++) samples[channel][i] = (float)Math.Sin(2 * Math.PI * 997 * i / SampleRate);
        return samples;
    }
}
