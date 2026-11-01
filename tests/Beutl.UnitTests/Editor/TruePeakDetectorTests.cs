using Beutl.Editor.Components.AudioVisualizerTab.Utilities;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TruePeakDetectorTests
{
    private const int SampleRate = 48000;

    // A low-frequency sine has no intersample overs, so its true peak equals its amplitude
    // whatever phase the block starts at (#3807: truncated taps at the edges read ~2 dB high).
    [TestCase(0.0)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    [TestCase(2.0)]
    [TestCase(3.0)]
    public void LowFrequencySineReadsItsAmplitude(double phase)
    {
        float[] samples = Sine(997, 0.5, phase, SampleRate * 4 / 10);

        float peak = TruePeakDetector.Detect(samples);

        Assert.That(20 * Math.Log10(peak / 0.5), Is.EqualTo(0).Within(0.1));
    }

    // At fs/4 with a 45° offset every sample sits at ±0.707, while the waveform reaches ±1.
    [Test]
    public void IntersamplePeakIsRecovered()
    {
        float[] samples = Sine(SampleRate / 4.0, 1.0, Math.PI / 4, 4800);

        float peak = TruePeakDetector.Detect(samples);

        Assert.That(samples.Max(Math.Abs), Is.EqualTo(0.7071f).Within(1e-3f));
        Assert.That(20 * Math.Log10(peak), Is.EqualTo(0).Within(0.1));
    }

    [Test]
    public void BlocksShorterThanTheFilterReadTheSamplePeak()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TruePeakDetector.Detect([]), Is.Zero);
            Assert.That(TruePeakDetector.Detect([0.1f, -0.6f, 0.3f]), Is.EqualTo(0.6f));
        });
    }

    private static float[] Sine(double frequency, double amplitude, double phase, int length)
        => Enumerable.Range(0, length)
            .Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / SampleRate + phase)))
            .ToArray();
}
